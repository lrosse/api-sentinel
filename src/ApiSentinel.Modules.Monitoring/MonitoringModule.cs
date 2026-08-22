using System.Security.Claims;
using ApiSentinel.Modules.Monitoring.Domain;
using ApiSentinel.Modules.Monitoring.HttpExecution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MonitorEntity = ApiSentinel.Modules.Monitoring.Domain.Monitor;

namespace ApiSentinel.Modules.Monitoring;

public static class MonitoringModule
{
    private const int MinimumTimeoutMs = 100;
    private const int MaximumTimeoutMs = 120_000;
    private const int MaximumIgnoredPaths = 100;
    private const int MaximumIgnoredPathLength = 512;

    public static IServiceCollection AddMonitoringModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MonitoringHttpOptions>()
            .Bind(configuration.GetSection(MonitoringHttpOptions.SectionName))
            .Validate(
                value => value.MaxResponseBytes is >= 1_024 and <= 10_485_760,
                "Monitoring:HttpExecution:MaxResponseBytes deve ficar entre 1 KB e 10 MB.")
            .Validate(
                value => value.ResponseBodySnippetMaxChars is >= 128 and <= 16_384,
                "Monitoring:HttpExecution:ResponseBodySnippetMaxChars está fora do limite.")
            .Validate(
                value => value.MaxRedirects is >= 1 and <= 10,
                "Monitoring:HttpExecution:MaxRedirects deve ficar entre 1 e 10.")
            .ValidateOnStart();
        services.AddOptions<NetworkSecurityOptions>()
            .Bind(configuration.GetSection(NetworkSecurityOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDnsAddressResolver, SystemDnsAddressResolver>();
        services.AddSingleton<ISsrfTargetValidator, SsrfTargetValidator>();
        services.AddSingleton<IMonitorExecutionGate, MonitorExecutionGate>();
        services.AddScoped<IHttpMonitorExecutor, HttpMonitorExecutor>();
        return services;
    }

    public static IEndpointRouteBuilder MapMonitoringModule(this IEndpointRouteBuilder endpoints)
    {
        var endpointMonitors = endpoints
            .MapGroup("/endpoints")
            .WithTags("Monitoring")
            .RequireAuthorization();
        endpointMonitors.MapPost("/{id:guid}/monitors", CreateMonitorAsync);
        endpointMonitors.MapGet("/{id:guid}/monitors", ListMonitorsAsync);

        var monitors = endpoints
            .MapGroup("/monitors")
            .WithTags("Monitoring")
            .RequireAuthorization();
        monitors.MapPut("/{id:guid}", UpdateMonitorAsync);
        monitors.MapDelete("/{id:guid}", DeleteMonitorAsync);
        monitors.MapPost("/{id:guid}/run", RunMonitorAsync);
        monitors.MapGet("/{id:guid}/runs", ListCheckRunsAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateMonitorAsync(
        Guid id,
        MonitorRequest request,
        ClaimsPrincipal principal,
        IMonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var errors = ValidateMonitor(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var endpoint = await dbContext.Endpoints
            .FirstOrDefaultAsync(
                candidate => candidate.Id == id &&
                             candidate.ApiService.OwnerUserId == ownerUserId,
                cancellationToken);

        if (endpoint is null)
        {
            return Results.NotFound();
        }

        var monitor = new MonitorEntity
        {
            Id = Guid.NewGuid(),
            EndpointId = endpoint.Id,
            Endpoint = endpoint,
            TimeoutMs = request.TimeoutMs,
            ExpectedStatusCode = request.ExpectedStatusCode,
            MaxLatencyMs = request.MaxLatencyMs,
            IgnoredPaths = NormalizeIgnoredPaths(request.IgnoredPaths)
        };

        dbContext.Monitors.Add(monitor);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/monitors/{monitor.Id}", ToResponse(monitor));
    }

    private static async Task<IResult> ListMonitorsAsync(
        Guid id,
        ClaimsPrincipal principal,
        IMonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var ownsEndpoint = await dbContext.Endpoints.AnyAsync(
            endpoint => endpoint.Id == id &&
                        endpoint.ApiService.OwnerUserId == ownerUserId,
            cancellationToken);

        if (!ownsEndpoint)
        {
            return Results.NotFound();
        }

        var monitors = await dbContext.Monitors
            .AsNoTracking()
            .Where(monitor => monitor.EndpointId == id)
            .OrderBy(monitor => monitor.TimeoutMs)
            .ThenBy(monitor => monitor.Id)
            .Select(monitor => ToResponse(monitor))
            .ToListAsync(cancellationToken);
        return Results.Ok(monitors);
    }

    private static async Task<IResult> UpdateMonitorAsync(
        Guid id,
        MonitorRequest request,
        ClaimsPrincipal principal,
        IMonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var errors = ValidateMonitor(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var monitor = await dbContext.Monitors.FirstOrDefaultAsync(
            candidate => candidate.Id == id &&
                         candidate.Endpoint.ApiService.OwnerUserId == ownerUserId,
            cancellationToken);
        if (monitor is null)
        {
            return Results.NotFound();
        }

        monitor.TimeoutMs = request.TimeoutMs;
        monitor.ExpectedStatusCode = request.ExpectedStatusCode;
        monitor.MaxLatencyMs = request.MaxLatencyMs;
        monitor.IgnoredPaths = NormalizeIgnoredPaths(request.IgnoredPaths);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(monitor));
    }

    private static async Task<IResult> DeleteMonitorAsync(
        Guid id,
        ClaimsPrincipal principal,
        IMonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var monitor = await dbContext.Monitors.FirstOrDefaultAsync(
            candidate => candidate.Id == id &&
                         candidate.Endpoint.ApiService.OwnerUserId == ownerUserId,
            cancellationToken);
        if (monitor is null)
        {
            return Results.NotFound();
        }

        dbContext.Monitors.Remove(monitor);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RunMonitorAsync(
        Guid id,
        ClaimsPrincipal principal,
        IMonitoringDbContext dbContext,
        IHttpMonitorExecutor executor,
        IMonitorExecutionGate executionGate,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var monitor = await dbContext.Monitors
            .Include(candidate => candidate.Endpoint)
            .ThenInclude(endpoint => endpoint.ApiService)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == id &&
                             candidate.Endpoint.ApiService.OwnerUserId == ownerUserId,
                cancellationToken);
        if (monitor is null)
        {
            return Results.NotFound();
        }

        using var lease = executionGate.TryEnter(id);
        if (lease is null)
        {
            return Results.Problem(
                title: "Este monitor já possui uma execução em andamento.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var checkRun = await executor.ExecuteAsync(monitor, cancellationToken);
        dbContext.CheckRuns.Add(checkRun);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/monitors/{id}/runs/{checkRun.Id}", ToResponse(checkRun));
    }

    private static async Task<IResult> ListCheckRunsAsync(
        Guid id,
        int? limit,
        ClaimsPrincipal principal,
        IMonitoringDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var ownsMonitor = await dbContext.Monitors.AnyAsync(
            candidate => candidate.Id == id &&
                         candidate.Endpoint.ApiService.OwnerUserId == ownerUserId,
            cancellationToken);
        if (!ownsMonitor)
        {
            return Results.NotFound();
        }

        var resultLimit = Math.Clamp(limit ?? 50, 1, 100);
        var runs = await dbContext.CheckRuns
            .AsNoTracking()
            .Where(run => run.MonitorId == id)
            .OrderByDescending(run => run.StartedAt)
            .Take(resultLimit)
            .Select(run => ToResponse(run))
            .ToListAsync(cancellationToken);
        return Results.Ok(runs);
    }

    private static Dictionary<string, string[]> ValidateMonitor(MonitorRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.TimeoutMs is < MinimumTimeoutMs or > MaximumTimeoutMs)
        {
            errors["timeoutMs"] =
                [$"O timeout deve ficar entre {MinimumTimeoutMs} e {MaximumTimeoutMs} ms."];
        }

        if (request.ExpectedStatusCode is < 100 or > 599)
        {
            errors["expectedStatusCode"] = ["O status esperado deve ficar entre 100 e 599."];
        }

        if (request.MaxLatencyMs is <= 0 or > MaximumTimeoutMs)
        {
            errors["maxLatencyMs"] =
                [$"A latência máxima deve ficar entre 1 e {MaximumTimeoutMs} ms."];
        }

        var ignoredPaths = NormalizeIgnoredPaths(request.IgnoredPaths);
        if (ignoredPaths.Count > MaximumIgnoredPaths)
        {
            errors["ignoredPaths"] =
                [$"Informe no máximo {MaximumIgnoredPaths} paths ignorados."];
        }
        else if (ignoredPaths.Any(path => path.Length > MaximumIgnoredPathLength))
        {
            errors["ignoredPaths"] =
                [$"Cada path ignorado deve ter no máximo {MaximumIgnoredPathLength} caracteres."];
        }

        return errors;
    }

    private static List<string> NormalizeIgnoredPaths(IEnumerable<string>? paths) =>
        paths?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

    private static MonitorResponse ToResponse(MonitorEntity monitor) =>
        new(
            monitor.Id,
            monitor.EndpointId,
            monitor.TimeoutMs,
            monitor.ExpectedStatusCode,
            monitor.MaxLatencyMs,
            monitor.IgnoredPaths);

    private static CheckRunResponse ToResponse(CheckRun run) =>
        new(
            run.Id,
            run.MonitorId,
            AsUtc(run.StartedAt),
            AsUtc(run.FinishedAt),
            run.Status,
            run.HttpStatusCode,
            run.LatencyMs,
            run.ErrorMessage,
            run.ResponseBodySnippet);

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public sealed record MonitorRequest(
        int TimeoutMs,
        int ExpectedStatusCode,
        int? MaxLatencyMs,
        IReadOnlyCollection<string>? IgnoredPaths);

    public sealed record MonitorResponse(
        Guid Id,
        Guid EndpointId,
        int TimeoutMs,
        int ExpectedStatusCode,
        int? MaxLatencyMs,
        IReadOnlyCollection<string> IgnoredPaths);

    public sealed record CheckRunResponse(
        Guid Id,
        Guid MonitorId,
        DateTime StartedAt,
        DateTime FinishedAt,
        CheckRunStatus Status,
        int? HttpStatusCode,
        long LatencyMs,
        string? ErrorMessage,
        string? ResponseBodySnippet);
}
