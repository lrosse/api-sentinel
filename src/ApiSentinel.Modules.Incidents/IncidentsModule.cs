using System.Security.Claims;
using ApiSentinel.Modules.ApiCatalog.Domain;
using ApiSentinel.Modules.Incidents.Domain;
using ApiSentinel.Modules.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApiSentinel.Modules.Incidents;

public static class IncidentsModule
{
    private const int MaximumRootCauseLength = 4_000;

    public static IServiceCollection AddIncidentsModule(this IServiceCollection services)
    {
        services.AddScoped<IncidentLifecycleService>();
        services.Replace(ServiceDescriptor.Scoped<IMonitorRunIncidentEvaluator>(provider =>
            provider.GetRequiredService<IncidentLifecycleService>()));
        services.Replace(ServiceDescriptor.Scoped<IActiveIncidentReader>(provider =>
            provider.GetRequiredService<IncidentLifecycleService>()));
        return services;
    }

    public static IEndpointRouteBuilder MapIncidentsModule(this IEndpointRouteBuilder endpoints)
    {
        var incidents = endpoints
            .MapGroup("/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();
        incidents.MapGet("", ListIncidentsAsync);
        incidents.MapGet("/{id:guid}", GetIncidentAsync);
        incidents.MapPost("/{id:guid}/resolve", ResolveIncidentAsync);
        return endpoints;
    }

    private static async Task<IResult> ListIncidentsAsync(
        IncidentStatus? status,
        ClaimsPrincipal principal,
        IIncidentsDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var query = dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Monitor.Endpoint.ApiService.OwnerUserId == ownerUserId);
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        var rows = await query
            .OrderBy(incident => incident.Status == IncidentStatus.Open ? 0 :
                incident.Status == IncidentStatus.Recovered ? 1 : 2)
            .ThenByDescending(incident => incident.OpenedAt)
            .ThenByDescending(incident => incident.Id)
            .Select(incident => new IncidentListRow(
                incident.Id,
                incident.MonitorId,
                incident.Status,
                incident.OpenedAt,
                incident.RecoveredAt,
                incident.ResolvedAt,
                incident.TriggerReason,
                incident.RootCause,
                incident.Monitor.EndpointId,
                incident.Monitor.Endpoint.Method,
                incident.Monitor.Endpoint.Path,
                incident.Monitor.Endpoint.ApiServiceId,
                incident.Monitor.Endpoint.ApiService.Name))
            .ToListAsync(cancellationToken);

        return Results.Ok(rows.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetIncidentAsync(
        Guid id,
        ClaimsPrincipal principal,
        IIncidentsDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var incident = await dbContext.Incidents
            .AsNoTracking()
            .Include(candidate => candidate.Monitor)
            .ThenInclude(monitor => monitor.Endpoint)
            .ThenInclude(endpoint => endpoint.ApiService)
            .Include(candidate => candidate.Events)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == id &&
                             candidate.Monitor.Endpoint.ApiService.OwnerUserId == ownerUserId,
                cancellationToken);
        return incident is null ? Results.NotFound() : Results.Ok(ToDetailResponse(incident));
    }

    private static async Task<IResult> ResolveIncidentAsync(
        Guid id,
        ResolveIncidentRequest? request,
        ClaimsPrincipal principal,
        IIncidentsDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var incident = await dbContext.Incidents
            .Include(candidate => candidate.Monitor)
            .ThenInclude(monitor => monitor.Endpoint)
            .ThenInclude(endpoint => endpoint.ApiService)
            .Include(candidate => candidate.Events)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == id &&
                             candidate.Monitor.Endpoint.ApiService.OwnerUserId == ownerUserId,
                cancellationToken);
        if (incident is null)
        {
            return Results.NotFound();
        }

        var rootCause = string.IsNullOrWhiteSpace(request?.RootCause)
            ? null
            : request.RootCause.Trim();
        if (rootCause?.Length > MaximumRootCauseLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["rootCause"] =
                [$"A causa raiz deve ter no máximo {MaximumRootCauseLength} caracteres."]
            });
        }

        if (incident.Status == IncidentStatus.Resolved)
        {
            return Results.Problem(
                title: "Este incidente já está resolvido.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var resolvedAt = timeProvider.GetUtcNow().UtcDateTime;
        incident.Status = IncidentStatus.Resolved;
        incident.ResolvedAt = resolvedAt;
        incident.RootCause = rootCause;
        dbContext.IncidentEvents.Add(new IncidentEvent
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            OccurredAt = resolvedAt,
            EventType = IncidentEventType.ResolvedManually,
            Description = "Resolução confirmada manualmente pelo usuário.",
            Incident = incident
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToDetailResponse(incident));
    }

    private static IncidentListResponse ToResponse(IncidentListRow row) =>
        new(
            row.Id,
            row.MonitorId,
            row.Status,
            AsUtc(row.OpenedAt),
            AsUtc(row.RecoveredAt),
            AsUtc(row.ResolvedAt),
            row.TriggerReason,
            row.RootCause,
            new IncidentMonitorResponse(
                row.MonitorId,
                row.EndpointId,
                row.EndpointMethod.ToString(),
                row.EndpointPath,
                row.ApiServiceId,
                row.ApiServiceName));

    private static IncidentDetailResponse ToDetailResponse(Incident incident) =>
        new(
            incident.Id,
            incident.MonitorId,
            incident.Status,
            AsUtc(incident.OpenedAt),
            AsUtc(incident.RecoveredAt),
            AsUtc(incident.ResolvedAt),
            incident.TriggerReason,
            incident.RootCause,
            new IncidentMonitorResponse(
                incident.MonitorId,
                incident.Monitor.EndpointId,
                incident.Monitor.Endpoint.Method.ToString(),
                incident.Monitor.Endpoint.Path,
                incident.Monitor.Endpoint.ApiServiceId,
                incident.Monitor.Endpoint.ApiService.Name),
            incident.Events
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.Id)
                .Select(item => new IncidentEventResponse(
                    item.Id,
                    AsUtc(item.OccurredAt),
                    item.EventType,
                    item.Description,
                    item.RelatedCheckRunId,
                    item.RelatedContractChangeId))
                .ToList());

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) => value is null ? null : AsUtc(value.Value);

    public sealed record ResolveIncidentRequest(string? RootCause);

    private sealed record IncidentListRow(
        Guid Id,
        Guid MonitorId,
        IncidentStatus Status,
        DateTime OpenedAt,
        DateTime? RecoveredAt,
        DateTime? ResolvedAt,
        string TriggerReason,
        string? RootCause,
        Guid EndpointId,
        EndpointMethod EndpointMethod,
        string EndpointPath,
        Guid ApiServiceId,
        string ApiServiceName);

    public sealed record IncidentListResponse(
        Guid Id,
        Guid MonitorId,
        IncidentStatus Status,
        DateTime OpenedAt,
        DateTime? RecoveredAt,
        DateTime? ResolvedAt,
        string TriggerReason,
        string? RootCause,
        IncidentMonitorResponse Monitor);

    public sealed record IncidentDetailResponse(
        Guid Id,
        Guid MonitorId,
        IncidentStatus Status,
        DateTime OpenedAt,
        DateTime? RecoveredAt,
        DateTime? ResolvedAt,
        string TriggerReason,
        string? RootCause,
        IncidentMonitorResponse Monitor,
        IReadOnlyCollection<IncidentEventResponse> Events);

    public sealed record IncidentMonitorResponse(
        Guid Id,
        Guid EndpointId,
        string EndpointMethod,
        string EndpointPath,
        Guid ApiServiceId,
        string ApiServiceName);

    public sealed record IncidentEventResponse(
        Guid Id,
        DateTime OccurredAt,
        IncidentEventType EventType,
        string Description,
        Guid? RelatedCheckRunId,
        Guid? RelatedContractChangeId);
}
