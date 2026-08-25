using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiSentinel.Infrastructure.Persistence;
using ApiSentinel.Modules.Incidents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApiSentinel.Api.IntegrationTests;

public sealed class Marco5IntegrationTests :
    IClassFixture<ApiSentinelWebApplicationFactory>,
    IAsyncLifetime
{
    private const string ValidPassword = "Sentinel#2026";
    private const string ContractV1 = "{\"id\":1,\"nome\":\"Teclado\"}";
    private const string ContractBreaking = "{\"id\":\"1\"}";

    private readonly ApiSentinelWebApplicationFactory _factory;
    private readonly LocalHttpMockServer _mockServer = new();
    private string _responseBody = ContractV1;

    public Marco5IntegrationTests(ApiSentinelWebApplicationFactory factory)
    {
        _factory = factory;
        _mockServer.JsonBodyFactory = _ => Volatile.Read(ref _responseBody);
    }

    public Task InitializeAsync() => _mockServer.StartAsync();

    public Task DisposeAsync() => _mockServer.DisposeAsync().AsTask();

    [Fact]
    public async Task Incident_endpoints_require_authentication()
    {
        using var client = _factory.CreateApiClient();
        var incidentId = Guid.NewGuid();
        using var listResponse = await client.GetAsync("/incidents");
        using var detailResponse = await client.GetAsync($"/incidents/{incidentId}");
        using var resolveResponse = await client.PostAsync(
            $"/incidents/{incidentId}/resolve",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, resolveResponse.StatusCode);
    }

    [Fact]
    public async Task Complete_incident_flow_opens_adds_evidence_recovers_and_resolves()
    {
        using var owner = await RegisterAndLoginAsync();
        var (endpointId, monitorId) = await CreateCatalogAndMonitorAsync(
            owner,
            "/produtos?falhar=true",
            threshold: 3);

        var firstFailure = await RunAsync(owner, monitorId, "Failure");
        var secondFailure = await RunAsync(owner, monitorId, "Failure");
        Assert.Empty(await ListIncidentsAsync(owner, "Open"));

        var thresholdFailure = await RunAsync(owner, monitorId, "Failure");
        var openIncident = Assert.Single(await ListIncidentsAsync(owner, "Open"));
        var incidentId = openIncident.GetProperty("id").GetGuid();
        Assert.Equal(monitorId, openIncident.GetProperty("monitorId").GetGuid());
        Assert.Equal("Open", openIncident.GetProperty("status").GetString());
        Assert.Equal("3 falhas consecutivas.", openIncident.GetProperty("triggerReason").GetString());
        Assert.Equal(JsonValueKind.Null, openIncident.GetProperty("rootCause").ValueKind);

        var openDetail = await GetIncidentAsync(owner, incidentId);
        var openedEvent = Assert.Single(openDetail.GetProperty("events").EnumerateArray());
        Assert.Equal("Opened", openedEvent.GetProperty("eventType").GetString());
        Assert.Equal(
            thresholdFailure.GetProperty("id").GetGuid(),
            openedEvent.GetProperty("relatedCheckRunId").GetGuid());

        var evidenceFailure = await RunAsync(owner, monitorId, "Failure");
        var afterEvidence = await GetIncidentAsync(owner, incidentId);
        var evidenceEvents = afterEvidence.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(2, evidenceEvents.Length);
        Assert.Equal("EvidenceAdded", evidenceEvents[1].GetProperty("eventType").GetString());
        Assert.Equal(
            evidenceFailure.GetProperty("id").GetGuid(),
            evidenceEvents[1].GetProperty("relatedCheckRunId").GetGuid());
        Assert.Single(await ListIncidentsAsync(owner, "Open"));

        using var otherUser = await RegisterAndLoginAsync();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherUser.GetAsync($"/incidents/{incidentId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherUser.PostAsJsonAsync(
                $"/incidents/{incidentId}/resolve",
                new { rootCause = "Não deveria ser salvo" })).StatusCode);

        using var endpointUpdate = await owner.PutAsJsonAsync(
            $"/endpoints/{endpointId}",
            new { path = "/produtos", method = "GET" });
        Assert.Equal(HttpStatusCode.OK, endpointUpdate.StatusCode);

        await RunAsync(owner, monitorId, "Success");
        var recovered = await GetIncidentAsync(owner, incidentId);
        Assert.Equal("Recovered", recovered.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, recovered.GetProperty("recoveredAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, recovered.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(
            "Recovered",
            recovered.GetProperty("events").EnumerateArray().Last()
                .GetProperty("eventType").GetString());

        var activeIncident = await GetDashboardIncidentAsync(owner, monitorId);
        Assert.Equal(incidentId, activeIncident.GetProperty("id").GetGuid());
        Assert.Equal("Recovered", activeIncident.GetProperty("status").GetString());

        const string rootCause = "Dependência externa indisponível durante manutenção.";
        using var resolveResponse = await owner.PostAsJsonAsync(
            $"/incidents/{incidentId}/resolve",
            new { rootCause });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        var resolved = await ReadObjectAsync(resolveResponse);
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.Equal(rootCause, resolved.GetProperty("rootCause").GetString());
        Assert.NotEqual(JsonValueKind.Null, resolved.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(
            "ResolvedManually",
            resolved.GetProperty("events").EnumerateArray().Last()
                .GetProperty("eventType").GetString());
        Assert.Single(await ListIncidentsAsync(owner, "Resolved"));
        Assert.Equal(JsonValueKind.Null, (await GetDashboardMonitorAsync(owner, monitorId))
            .GetProperty("activeIncident").ValueKind);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await dbContext.CheckRuns.CountAsync(run => run.MonitorId == monitorId &&
            run.Status == ApiSentinel.Modules.Monitoring.Domain.CheckRunStatus.Failure));
        Assert.Single(await dbContext.Incidents.Where(incident => incident.MonitorId == monitorId)
            .ToListAsync());
        Assert.Equal(IncidentStatus.Resolved, await dbContext.Incidents
            .Where(incident => incident.Id == incidentId)
            .Select(incident => incident.Status)
            .SingleAsync());

        Assert.NotEqual(Guid.Empty, firstFailure.GetProperty("id").GetGuid());
        Assert.NotEqual(Guid.Empty, secondFailure.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Breaking_contract_change_opens_incident_immediately_below_failure_threshold()
    {
        using var owner = await RegisterAndLoginAsync();
        var (_, monitorId) = await CreateCatalogAndMonitorAsync(
            owner,
            "/produtos",
            threshold: 10);

        await RunAsync(owner, monitorId, "Success");
        Assert.Empty(await ListIncidentsAsync(owner, "Open"));

        Volatile.Write(ref _responseBody, ContractBreaking);
        var breakingRun = await RunAsync(owner, monitorId, "Success");

        var incident = Assert.Single(await ListIncidentsAsync(owner, "Open"));
        Assert.Contains(
            "Mudança de contrato quebradora detectada em id",
            incident.GetProperty("triggerReason").GetString());
        var detail = await GetIncidentAsync(owner, incident.GetProperty("id").GetGuid());
        var opened = Assert.Single(detail.GetProperty("events").EnumerateArray());
        Assert.Equal("Opened", opened.GetProperty("eventType").GetString());
        Assert.Equal(
            breakingRun.GetProperty("id").GetGuid(),
            opened.GetProperty("relatedCheckRunId").GetGuid());
        Assert.NotEqual(
            JsonValueKind.Null,
            opened.GetProperty("relatedContractChangeId").ValueKind);
    }

    [Fact]
    public async Task Open_incident_can_be_resolved_manually_without_automatic_recovery()
    {
        using var owner = await RegisterAndLoginAsync();
        var (_, monitorId) = await CreateCatalogAndMonitorAsync(
            owner,
            "/produtos?falhar=true",
            threshold: 1);
        await RunAsync(owner, monitorId, "Failure");
        var incidentId = Assert.Single(await ListIncidentsAsync(owner, "Open"))
            .GetProperty("id").GetGuid();

        using var response = await owner.PostAsync($"/incidents/{incidentId}/resolve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolved = await ReadObjectAsync(response);
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, resolved.GetProperty("recoveredAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, resolved.GetProperty("rootCause").ValueKind);
    }

    [Fact]
    public async Task Monitor_threshold_rejects_zero_and_defaults_to_three()
    {
        using var owner = await RegisterAndLoginAsync();
        var endpointId = await CreateCatalogAsync(owner, "/produtos");

        using var invalidResponse = await owner.PostAsJsonAsync(
            $"/endpoints/{endpointId}/monitors",
            MonitorBody(threshold: 0));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Contains("consecutiveFailuresThreshold", await invalidResponse.Content.ReadAsStringAsync());

        using var defaultResponse = await owner.PostAsJsonAsync(
            $"/endpoints/{endpointId}/monitors",
            new
            {
                timeoutMs = 5_000,
                expectedStatusCode = 200,
                maxLatencyMs = (int?)null,
                intervalSeconds = 300,
                enabled = false,
                ignoredPaths = Array.Empty<string>()
            });
        Assert.Equal(HttpStatusCode.Created, defaultResponse.StatusCode);
        Assert.Equal(
            3,
            (await ReadObjectAsync(defaultResponse))
                .GetProperty("consecutiveFailuresThreshold").GetInt32());
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

    private async Task<(Guid EndpointId, Guid MonitorId)> CreateCatalogAndMonitorAsync(
        HttpClient client,
        string path,
        int threshold)
    {
        var endpointId = await CreateCatalogAsync(client, path);
        using var monitorResponse = await client.PostAsJsonAsync(
            $"/endpoints/{endpointId}/monitors",
            MonitorBody(threshold));
        Assert.Equal(HttpStatusCode.Created, monitorResponse.StatusCode);
        var monitor = await ReadObjectAsync(monitorResponse);
        Assert.Equal(threshold, monitor.GetProperty("consecutiveFailuresThreshold").GetInt32());
        return (endpointId, monitor.GetProperty("id").GetGuid());
    }

    private async Task<Guid> CreateCatalogAsync(HttpClient client, string path)
    {
        using var serviceResponse = await client.PostAsJsonAsync(
            "/api-services",
            new
            {
                name = $"API Marco 5 {Guid.NewGuid():N}",
                description = "Fluxo completo de incidentes",
                tags = new[] { "incidents" },
                baseUrl = $"http://mock-api-1:{_mockServer.Port}"
            });
        Assert.Equal(HttpStatusCode.Created, serviceResponse.StatusCode);
        var serviceId = (await ReadObjectAsync(serviceResponse)).GetProperty("id").GetGuid();

        using var endpointResponse = await client.PostAsJsonAsync(
            $"/api-services/{serviceId}/endpoints",
            new { path, method = "GET" });
        Assert.Equal(HttpStatusCode.Created, endpointResponse.StatusCode);
        return (await ReadObjectAsync(endpointResponse)).GetProperty("id").GetGuid();
    }

    private static object MonitorBody(int threshold) => new
    {
        timeoutMs = 5_000,
        expectedStatusCode = 200,
        maxLatencyMs = (int?)null,
        consecutiveFailuresThreshold = threshold,
        intervalSeconds = 300,
        enabled = false,
        ignoredPaths = Array.Empty<string>()
    };

    private static async Task<JsonElement> RunAsync(
        HttpClient client,
        Guid monitorId,
        string expectedStatus)
    {
        using var response = await client.PostAsync($"/monitors/{monitorId}/run", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await ReadObjectAsync(response);
        Assert.Equal(expectedStatus, run.GetProperty("status").GetString());
        return run;
    }

    private static async Task<JsonElement[]> ListIncidentsAsync(
        HttpClient client,
        string? status = null)
    {
        var path = status is null ? "/incidents" : $"/incidents?status={status}";
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static async Task<JsonElement> GetIncidentAsync(HttpClient client, Guid incidentId)
    {
        using var response = await client.GetAsync($"/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadObjectAsync(response);
    }

    private static async Task<JsonElement> GetDashboardMonitorAsync(
        HttpClient client,
        Guid monitorId)
    {
        using var response = await client.GetAsync("/dashboard/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement
            .EnumerateArray()
            .SelectMany(apiService => apiService.GetProperty("monitors").EnumerateArray())
            .Single(monitor => monitor.GetProperty("id").GetGuid() == monitorId)
            .Clone();
    }

    private static async Task<JsonElement> GetDashboardIncidentAsync(
        HttpClient client,
        Guid monitorId) =>
        (await GetDashboardMonitorAsync(client, monitorId)).GetProperty("activeIncident").Clone();

    private static async Task<JsonElement> ReadObjectAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
