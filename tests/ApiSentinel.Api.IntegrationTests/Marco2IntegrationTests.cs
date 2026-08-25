using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ApiSentinel.Api.IntegrationTests;

public sealed class Marco2IntegrationTests :
    IClassFixture<ApiSentinelWebApplicationFactory>,
    IAsyncLifetime
{
    private const string ValidPassword = "Sentinel#2026";
    private readonly ApiSentinelWebApplicationFactory _factory;
    private readonly LocalHttpMockServer _mockServer = new();

    public Marco2IntegrationTests(ApiSentinelWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => _mockServer.StartAsync();

    public Task DisposeAsync() => _mockServer.DisposeAsync().AsTask();

    public static TheoryData<string, HttpMethod, object?> ProtectedMonitoringRequests => new()
    {
        { $"/endpoints/{Guid.NewGuid()}/monitors", HttpMethod.Post, ValidMonitorBody() },
        { $"/endpoints/{Guid.NewGuid()}/monitors", HttpMethod.Get, null },
        { $"/monitors/{Guid.NewGuid()}", HttpMethod.Put, ValidMonitorBody() },
        { $"/monitors/{Guid.NewGuid()}", HttpMethod.Delete, null },
        { $"/monitors/{Guid.NewGuid()}/run", HttpMethod.Post, null },
        { $"/monitors/{Guid.NewGuid()}/runs", HttpMethod.Get, null },
        { $"/monitors/{Guid.NewGuid()}/contract-changes", HttpMethod.Get, null },
        { $"/monitors/{Guid.NewGuid()}/schema-snapshot/latest", HttpMethod.Get, null },
        { "/dashboard/summary", HttpMethod.Get, null }
    };

    [Theory]
    [MemberData(nameof(ProtectedMonitoringRequests))]
    public async Task Monitoring_endpoints_require_authentication(
        string path,
        HttpMethod method,
        object? body)
    {
        using var client = _factory.CreateApiClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Monitor_crud_supports_multiple_monitors_per_endpoint()
    {
        using var client = await RegisterAndLoginAsync();
        var endpointId = await CreateCatalogAsync(
            client,
            $"http://mock-api-1:{_mockServer.Port}",
            "/ok");

        var firstId = await CreateMonitorAsync(client, endpointId, timeoutMs: 1_000);
        var secondId = await CreateMonitorAsync(client, endpointId, timeoutMs: 5_000);

        using var listResponse = await client.GetAsync($"/endpoints/{endpointId}/monitors");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(2, (await ReadArrayAsync(listResponse)).Length);

        using var updateResponse = await client.PutAsJsonAsync(
            $"/monitors/{firstId}",
            ValidMonitorBody(2_000, 204, 1_500, ["cliente.id", "metadata.timestamp"]));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updated = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal(2_000, updated.RootElement.GetProperty("timeoutMs").GetInt32());
        Assert.Equal(204, updated.RootElement.GetProperty("expectedStatusCode").GetInt32());
        Assert.Equal(2, updated.RootElement.GetProperty("ignoredPaths").GetArrayLength());

        using var deleteResponse = await client.DeleteAsync($"/monitors/{secondId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        using var listAfterDelete = await client.GetAsync($"/endpoints/{endpointId}/monitors");
        Assert.Single(await ReadArrayAsync(listAfterDelete));
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    public async Task Ssrf_blocks_private_and_cloud_metadata_addresses(string blockedAddress)
    {
        using var client = await RegisterAndLoginAsync();
        var endpointId = await CreateCatalogAsync(client, $"http://{blockedAddress}", "/probe");
        var monitorId = await CreateMonitorAsync(client, endpointId);

        using var response = await client.PostAsync($"/monitors/{monitorId}/run", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await ReadObjectAsync(response);
        Assert.Equal("Failure", run.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("httpStatusCode").ValueKind);
        Assert.Contains("política SSRF", run.GetProperty("errorMessage").GetString());

        using var historyResponse = await client.GetAsync($"/monitors/{monitorId}/runs");
        var history = await ReadArrayAsync(historyResponse);
        Assert.Single(history);
        Assert.Equal(run.GetProperty("id").GetGuid(), history[0].GetProperty("id").GetGuid());
        Assert.EndsWith("Z", history[0].GetProperty("startedAt").GetString());
    }

    [Theory]
    [InlineData("mock-api-1")]
    [InlineData("mock-api-2")]
    public async Task Development_allowlist_permits_known_mock_hosts(string mockHost)
    {
        using var client = await RegisterAndLoginAsync();
        var endpointId = await CreateCatalogAsync(
            client,
            $"http://{mockHost}:{_mockServer.Port}",
            "/ok");
        var monitorId = await CreateMonitorAsync(client, endpointId);

        using var response = await client.PostAsync($"/monitors/{monitorId}/run", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await ReadObjectAsync(response);
        Assert.Equal("Success", run.GetProperty("status").GetString());
        Assert.Equal(200, run.GetProperty("httpStatusCode").GetInt32());
        var snippet = run.GetProperty("responseBodySnippet").GetString();
        Assert.Contains("[REDACTED]", snippet);
        Assert.DoesNotContain("segredo-de-teste", snippet);
    }

    [Fact]
    public async Task Timeout_creates_a_failure_run_without_hanging_the_application()
    {
        using var client = await RegisterAndLoginAsync();
        var endpointId = await CreateCatalogAsync(
            client,
            $"http://mock-api-1:{_mockServer.Port}",
            "/produtos?atrasar=true");
        var monitorId = await CreateMonitorAsync(client, endpointId, timeoutMs: 1_000);

        using var response = await client.PostAsync($"/monitors/{monitorId}/run", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await ReadObjectAsync(response);
        Assert.Equal("Failure", run.GetProperty("status").GetString());
        Assert.Contains("timeout de 1000 ms", run.GetProperty("errorMessage").GetString());
        Assert.InRange(run.GetProperty("latencyMs").GetInt64(), 800, 2_500);
    }

    [Fact]
    public async Task Response_larger_than_one_megabyte_is_aborted_and_recorded_as_failure()
    {
        using var client = await RegisterAndLoginAsync();
        var endpointId = await CreateCatalogAsync(
            client,
            $"http://mock-api-1:{_mockServer.Port}",
            "/produtos?grande=true");
        var monitorId = await CreateMonitorAsync(client, endpointId);

        using var response = await client.PostAsync($"/monitors/{monitorId}/run", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await ReadObjectAsync(response);
        Assert.Equal("Failure", run.GetProperty("status").GetString());
        Assert.Contains("limite de 1048576 bytes", run.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task Two_parallel_runs_for_the_same_monitor_create_only_one_check_run()
    {
        using var client = await RegisterAndLoginAsync();
        const string slowPath = "/concorrencia?atrasar=true";
        var endpointId = await CreateCatalogAsync(
            client,
            $"http://mock-api-1:{_mockServer.Port}",
            slowPath);
        var monitorId = await CreateMonitorAsync(client, endpointId, timeoutMs: 5_000);

        var firstRequest = client.PostAsync($"/monitors/{monitorId}/run", null);
        await _mockServer.WaitForRequestAsync(slowPath);
        using var secondResponse = await client.PostAsync($"/monitors/{monitorId}/run", null);
        using var firstResponse = await firstRequest;

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Contains("execução em andamento", await secondResponse.Content.ReadAsStringAsync());

        using var historyResponse = await client.GetAsync($"/monitors/{monitorId}/runs");
        Assert.Single(await ReadArrayAsync(historyResponse));
    }

    [Fact]
    public async Task Another_user_cannot_manage_run_or_read_a_monitor()
    {
        using var owner = await RegisterAndLoginAsync();
        using var otherUser = await RegisterAndLoginAsync();
        var endpointId = await CreateCatalogAsync(
            owner,
            $"http://mock-api-1:{_mockServer.Port}",
            "/ok");
        var monitorId = await CreateMonitorAsync(owner, endpointId);

        using var listResponse = await otherUser.GetAsync($"/endpoints/{endpointId}/monitors");
        using var updateResponse = await otherUser.PutAsJsonAsync(
            $"/monitors/{monitorId}",
            ValidMonitorBody());
        using var runResponse = await otherUser.PostAsync($"/monitors/{monitorId}/run", null);
        using var historyResponse = await otherUser.GetAsync($"/monitors/{monitorId}/runs");
        using var deleteResponse = await otherUser.DeleteAsync($"/monitors/{monitorId}");

        Assert.Equal(HttpStatusCode.NotFound, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, runResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, historyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        using var ownerListResponse = await owner.GetAsync($"/endpoints/{endpointId}/monitors");
        Assert.Single(await ReadArrayAsync(ownerListResponse));
    }

    private async Task<HttpClient> RegisterAndLoginAsync()
    {
        var client = _factory.CreateApiClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        using var registerResponse = await client.PostAsJsonAsync(
            "/auth/register",
            new { email, password = ValidPassword });
        using var loginResponse = await client.PostAsJsonAsync(
            "/auth/login",
            new { email, password = ValidPassword });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        return client;
    }

    private static async Task<Guid> CreateCatalogAsync(
        HttpClient client,
        string baseUrl,
        string path)
    {
        using var serviceResponse = await client.PostAsJsonAsync(
            "/api-services",
            new
            {
                name = $"API {Guid.NewGuid():N}",
                description = "Mock do Marco 2",
                tags = new[] { "monitoring" },
                baseUrl
            });
        Assert.Equal(HttpStatusCode.Created, serviceResponse.StatusCode);
        var serviceId = (await ReadObjectAsync(serviceResponse)).GetProperty("id").GetGuid();

        using var endpointResponse = await client.PostAsJsonAsync(
            $"/api-services/{serviceId}/endpoints",
            new { path, method = "GET" });
        Assert.Equal(HttpStatusCode.Created, endpointResponse.StatusCode);
        return (await ReadObjectAsync(endpointResponse)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateMonitorAsync(
        HttpClient client,
        Guid endpointId,
        int timeoutMs = 5_000)
    {
        using var response = await client.PostAsJsonAsync(
            $"/endpoints/{endpointId}/monitors",
            ValidMonitorBody(timeoutMs));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadObjectAsync(response)).GetProperty("id").GetGuid();
    }

    private static object ValidMonitorBody(
        int timeoutMs = 5_000,
        int expectedStatusCode = 200,
        int? maxLatencyMs = null,
        string[]? ignoredPaths = null) => new
        {
            timeoutMs,
            expectedStatusCode,
            maxLatencyMs,
            ignoredPaths = ignoredPaths ?? Array.Empty<string>()
        };

    private static async Task<JsonElement> ReadObjectAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement[]> ReadArrayAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
    }
}

internal sealed class LocalHttpMockServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _requestSignals = new();
    private readonly ConcurrentBag<Task> _connectionTasks = [];
    private Task? _acceptLoop;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public Func<string, string>? JsonBodyFactory { get; set; }

    public Task StartAsync()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
        return Task.CompletedTask;
    }

    public async Task WaitForRequestAsync(string target)
    {
        var signal = _requestSignals.GetOrAdd(
            target,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            await _acceptLoop;
        }

        await Task.WhenAll(_connectionTasks.ToArray()).WaitAsync(TimeSpan.FromSeconds(5));
        _stopping.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                _connectionTasks.Add(HandleConnectionAsync(client));
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (SocketException) when (_stopping.IsCancellationRequested)
        {
        }
    }

    private async Task HandleConnectionAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(
                    stream,
                    Encoding.ASCII,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(_stopping.Token);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    return;
                }

                string? header;
                do
                {
                    header = await reader.ReadLineAsync(_stopping.Token);
                }
                while (!string.IsNullOrEmpty(header));

                var target = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
                _requestSignals.GetOrAdd(
                    target,
                    static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
                    .TrySetResult();

                if (target.Contains("atrasar=true", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), _stopping.Token);
                }

                byte[] body;
                string contentType;
                if (target.Contains("grande=true", StringComparison.OrdinalIgnoreCase))
                {
                    body = new byte[1_100_000];
                    Array.Fill(body, (byte)'x');
                    contentType = "text/plain";
                }
                else
                {
                    body = Encoding.UTF8.GetBytes(
                        JsonBodyFactory?.Invoke(target) ??
                        "{\"ok\":true,\"token\":\"segredo-de-teste\"}");
                    contentType = "application/json";
                }

                var headers = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    $"Content-Type: {contentType}\r\n" +
                    $"Content-Length: {body.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(headers, _stopping.Token);
                await stream.WriteAsync(body, _stopping.Token);
            }
            catch (Exception exception) when (
                exception is IOException or OperationCanceledException or SocketException)
            {
            }
        }
    }
}
