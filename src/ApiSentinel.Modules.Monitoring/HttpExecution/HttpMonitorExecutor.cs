using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using ApiSentinel.Modules.ApiCatalog.Domain;
using ApiSentinel.Modules.Monitoring.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorEntity = ApiSentinel.Modules.Monitoring.Domain.Monitor;

namespace ApiSentinel.Modules.Monitoring.HttpExecution;

internal interface IHttpMonitorExecutor
{
    Task<CheckRun> ExecuteAsync(MonitorEntity monitor, CancellationToken cancellationToken);
}

internal sealed partial class HttpMonitorExecutor(
    ISsrfTargetValidator targetValidator,
    IOptions<MonitoringHttpOptions> options,
    TimeProvider timeProvider,
    ILogger<HttpMonitorExecutor> logger) : IHttpMonitorExecutor
{
    public async Task<CheckRun> ExecuteAsync(
        MonitorEntity monitor,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var result = new CheckRun
        {
            Id = Guid.NewGuid(),
            MonitorId = monitor.Id,
            Monitor = monitor,
            StartedAt = startedAt.UtcDateTime,
            FinishedAt = startedAt.UtcDateTime,
            Status = CheckRunStatus.Failure,
            LatencyMs = 0
        };
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(monitor.TimeoutMs));

        try
        {
            var targetUri = BuildTargetUri(monitor.Endpoint.ApiService.BaseUrl, monitor.Endpoint.Path);
            await ValidateTargetUriAsync(targetUri, timeoutSource.Token);

            using var handler = CreateHandler();
            using var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            using var request = new HttpRequestMessage(ToHttpMethod(monitor.Endpoint.Method), targetUri);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            result.HttpStatusCode = (int)response.StatusCode;
            var body = await ReadResponseBodyAsync(response, timeoutSource.Token);
            result.ResponseBodySnippet = body.Snippet;

            if (body.ExceededLimit)
            {
                result.ErrorMessage =
                    $"A resposta excedeu o limite de {options.Value.MaxResponseBytes} bytes.";
            }
            else if (result.HttpStatusCode != monitor.ExpectedStatusCode)
            {
                result.ErrorMessage =
                    $"Status HTTP inesperado: recebido {result.HttpStatusCode}, esperado {monitor.ExpectedStatusCode}.";
            }
            else
            {
                result.Status = CheckRunStatus.Success;
            }
        }
        catch (UnsafeTargetException exception)
        {
            result.ErrorMessage = exception.Message;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.ErrorMessage = $"A execução excedeu o timeout de {monitor.TimeoutMs} ms.";
        }
        catch (HttpRequestException exception) when (
            exception.Message.Contains("redirect", StringComparison.OrdinalIgnoreCase))
        {
            result.ErrorMessage =
                $"A resposta excedeu o limite de {options.Value.MaxRedirects} redirecionamentos.";
        }
        catch (HttpRequestException)
        {
            result.ErrorMessage = "Falha de rede ao conectar ou ler a resposta do destino.";
        }
        catch (SocketException)
        {
            result.ErrorMessage = "Falha de rede ao conectar ao destino.";
        }
        catch (IOException)
        {
            result.ErrorMessage = "Falha de rede ao ler a resposta do destino.";
        }
        catch (UriFormatException)
        {
            result.ErrorMessage = "A URL resultante do endpoint é inválida.";
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "A execução foi cancelada antes de ser concluída.";
        }
        finally
        {
            stopwatch.Stop();
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            result.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;

            // Request/response headers and body content are deliberately absent from logs.
            // A query string may carry a credential, so the full target URI is not logged either.
            logger.LogInformation(
                "Manual check {CheckRunId} for monitor {MonitorId} finished with {Status} in {LatencyMs} ms.",
                result.Id,
                monitor.Id,
                result.Status,
                result.LatencyMs);
        }

        return result;
    }

    private SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = options.Value.MaxRedirects,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        UseProxy = false,
        ConnectCallback = ConnectToValidatedAddressAsync
    };

    private async ValueTask<Stream> ConnectToValidatedAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        // DNS is intentionally resolved again here. The socket connects to the validated IP
        // directly, leaving no second DNS lookup where rebinding could swap in a private address.
        var addresses = await targetValidator.ResolveAllowedAddressesAsync(
            context.DnsEndPoint.Host,
            cancellationToken);
        Exception? lastException = null;

        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                lastException = exception;
                socket.Dispose();
            }
        }

        throw new HttpRequestException("Não foi possível conectar a um IP validado.", lastException);
    }

    private async Task ValidateTargetUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.DnsSafeHost) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new UnsafeTargetException(
                "Somente URLs HTTP/HTTPS absolutas e sem credenciais embutidas são permitidas.");
        }

        await targetValidator.ResolveAllowedAddressesAsync(uri.DnsSafeHost, cancellationToken);
    }

    private async Task<ResponseBodyReadResult> ReadResponseBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var maximumBytes = options.Value.MaxResponseBytes;
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            return new ResponseBodyReadResult(null, true);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[81_920];
        using var snippetBytes = new MemoryStream(
            Math.Min(maximumBytes, options.Value.ResponseBodySnippetMaxChars * 4));
        var totalBytes = 0L;

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            var remainingSnippetBytes = Math.Max(
                0,
                options.Value.ResponseBodySnippetMaxChars * 4 - (int)snippetBytes.Length);
            if (remainingSnippetBytes > 0)
            {
                snippetBytes.Write(buffer, 0, Math.Min(bytesRead, remainingSnippetBytes));
            }

            if (totalBytes > maximumBytes)
            {
                return new ResponseBodyReadResult(
                    CreateSafeSnippet(response, snippetBytes.ToArray()),
                    true);
            }
        }

        return new ResponseBodyReadResult(
            CreateSafeSnippet(response, snippetBytes.ToArray()),
            false);
    }

    private string? CreateSafeSnippet(HttpResponseMessage response, byte[] bytes)
    {
        if (bytes.Length == 0 || !IsTextualContent(response.Content.Headers.ContentType?.MediaType))
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(bytes)
            .Replace('\0', ' ')
            .Trim();
        text = SensitiveJsonPropertyRegex().Replace(text, "$1\"[REDACTED]\"");
        return text.Length <= options.Value.ResponseBodySnippetMaxChars
            ? text
            : text[..options.Value.ResponseBodySnippetMaxChars];
    }

    private static bool IsTextualContent(string? mediaType) =>
        mediaType is not null &&
        (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
         mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
         mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
         mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
         mediaType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase));

    private static Uri BuildTargetUri(string baseUrl, string path)
    {
        var baseUri = new Uri(baseUrl, UriKind.Absolute);
        return new Uri(baseUri, path);
    }

    private static HttpMethod ToHttpMethod(EndpointMethod method) => method switch
    {
        EndpointMethod.GET => HttpMethod.Get,
        EndpointMethod.POST => HttpMethod.Post,
        EndpointMethod.PUT => HttpMethod.Put,
        EndpointMethod.PATCH => HttpMethod.Patch,
        EndpointMethod.DELETE => HttpMethod.Delete,
        _ => throw new InvalidOperationException("Método HTTP não suportado.")
    };

    [GeneratedRegex(
        "(?i)(\"(?:password|senha|token|access_token|refresh_token|authorization|cookie|secret|api[-_]?key)\"\\s*:\\s*)(\"(?:\\\\.|[^\"])*\"|[^,}\\s]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveJsonPropertyRegex();

    private sealed record ResponseBodyReadResult(string? Snippet, bool ExceededLimit);
}
