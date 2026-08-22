using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiSentinel.Infrastructure.Persistence;
using ApiSentinel.Modules.Monitoring.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ApiSentinel.Api.IntegrationTests;

public sealed class Marco3IntegrationTests : IAsyncLifetime
{
    private const string ValidPassword = "Sentinel#2026";
    private readonly ScheduledApiSentinelWebApplicationFactory _factory = new();
    private readonly LocalHttpMockServer _mockServer = new();

    public Task InitializeAsync() => _mockServer.StartAsync();

    public async Task DisposeAsync()
    {
        await _mockServer.DisposeAsync();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Enabled_monitor_creates_check_runs_automatically_without_manual_request()
    {
        using var client = await RegisterAndLoginAsync();
        var endpointId = await CreateCatalogAsync(client, "API automática", "/automatico");
        var monitorId = await CreateMonitorAsync(client, endpointId, enabled: true);

        var runs = await WaitForRunCountAsync(client, monitorId, minimumCount: 2);

        Assert.True(runs.Length >= 2);
        Assert.All(runs, run => Assert.Equal("Success", run.GetProperty("status").GetString()));
        Assert.True(GetScheduler().HasSchedule(monitorId));
    }

    [Fact]
    public async Task Deleting_enabled_monitor_removes_schedule_and_prevents_orphan_runs()
    {
        using var client = await RegisterAndLoginAsync();
        var endpointId = await CreateCatalogAsync(client, "API removida", "/removido");
        var monitorId = await CreateMonitorAsync(client, endpointId, enabled: true);
        await WaitForRunCountAsync(client, monitorId, minimumCount: 1);

        using var deleteResponse = await client.DeleteAsync($"/monitors/{monitorId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.False(GetScheduler().HasSchedule(monitorId));

        await Task.Delay(TestMonitorScheduleManager.TickInterval * 3);
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await dbContext.CheckRuns.AnyAsync(run => run.MonitorId == monitorId));
    }

    [Fact]
    public async Task Pausing_monitor_stops_automatic_runs_and_keeps_history_accessible()
    {
        using var client = await RegisterAndLoginAsync();
        var endpointId = await CreateCatalogAsync(client, "API pausada", "/pausado");
        var monitorId = await CreateMonitorAsync(client, endpointId, enabled: true);
        await WaitForRunCountAsync(client, monitorId, minimumCount: 1);

        using var pauseResponse = await client.PutAsJsonAsync(
            $"/monitors/{monitorId}",
            ValidMonitorBody(enabled: false));
        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);
        Assert.False(GetScheduler().HasSchedule(monitorId));

        var countAfterPause = (await GetRunsAsync(client, monitorId)).Length;
        await Task.Delay(TestMonitorScheduleManager.TickInterval * 3);
        var history = await GetRunsAsync(client, monitorId);

        Assert.True(countAfterPause > 0);
        Assert.Equal(countAfterPause, history.Length);

        using var resumeResponse = await client.PutAsJsonAsync(
            $"/monitors/{monitorId}",
            ValidMonitorBody(enabled: true));
        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        var resumedHistory = await WaitForRunCountAsync(
            client,
            monitorId,
            minimumCount: countAfterPause + 1);
        Assert.True(resumedHistory.Length > countAfterPause);
    }

    [Fact]
    public async Task Manual_and_scheduled_execution_share_the_same_concurrency_gate()
    {
        using var client = await RegisterAndLoginAsync();
        const string slowPath = "/agendado-concorrente?atrasar=true";
        var endpointId = await CreateCatalogAsync(client, "API concorrente", slowPath);
        var monitorId = await CreateMonitorAsync(
            client,
            endpointId,
            enabled: true,
            timeoutMs: 5_000);

        await _mockServer.WaitForRequestAsync(slowPath);
        using var manualResponse = await client.PostAsync($"/monitors/{monitorId}/run", null);

        Assert.Equal(HttpStatusCode.Conflict, manualResponse.StatusCode);
        Assert.Contains("execução em andamento", await manualResponse.Content.ReadAsStringAsync());

        await WaitForRunCountAsync(client, monitorId, minimumCount: 1);
        using var pauseResponse = await client.PutAsJsonAsync(
            $"/monitors/{monitorId}",
            ValidMonitorBody(timeoutMs: 5_000, enabled: false));
        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);
        Assert.Single(await GetRunsAsync(client, monitorId));
    }

    [Fact]
    public async Task Dashboard_summary_is_aggregated_and_isolated_by_authenticated_owner()
    {
        using var owner = await RegisterAndLoginAsync();
        using var otherUser = await RegisterAndLoginAsync();
        var ownerEndpointId = await CreateCatalogAsync(owner, "API do proprietário", "/owner");
        var otherEndpointId = await CreateCatalogAsync(otherUser, "API de outro usuário", "/other");
        var ownerMonitorId = await CreateMonitorAsync(
            owner,
            ownerEndpointId,
            enabled: false,
            expectedStatusCode: 201);
        await CreateMonitorAsync(otherUser, otherEndpointId, enabled: false);

        using var firstFailure = await owner.PostAsync($"/monitors/{ownerMonitorId}/run", null);
        using var secondFailure = await owner.PostAsync($"/monitors/{ownerMonitorId}/run", null);
        Assert.Equal(HttpStatusCode.Created, firstFailure.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondFailure.StatusCode);

        using var response = await owner.GetAsync("/dashboard/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var services = await ReadArrayAsync(response);

        var apiService = Assert.Single(services);
        Assert.Equal("API do proprietário", apiService.GetProperty("name").GetString());
        var monitor = Assert.Single(
            apiService.GetProperty("monitors").EnumerateArray().Select(item => item.Clone()));
        Assert.Equal(ownerMonitorId, monitor.GetProperty("id").GetGuid());
        Assert.Equal("Failure", monitor.GetProperty("lastRun").GetProperty("status").GetString());
        Assert.Equal(2, monitor.GetProperty("consecutiveFailures").GetInt32());
    }

    [Fact]
    public async Task Interval_below_hangfire_minimum_is_rejected_by_backend()
    {
        using var client = await RegisterAndLoginAsync();
        var endpointId = await CreateCatalogAsync(client, "API inválida", "/intervalo");

        using var response = await client.PostAsJsonAsync(
            $"/endpoints/{endpointId}/monitors",
            ValidMonitorBody(intervalSeconds: 30));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("intervalSeconds", await response.Content.ReadAsStringAsync());
    }

    private TestMonitorScheduleManager GetScheduler() =>
        _factory.Services.GetRequiredService<TestMonitorScheduleManager>();

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

    private async Task<Guid> CreateCatalogAsync(HttpClient client, string name, string path)
    {
        using var serviceResponse = await client.PostAsJsonAsync(
            "/api-services",
            new
            {
                name,
                description = "Teste do Marco 3",
                tags = new[] { "scheduling" },
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

    private static async Task<Guid> CreateMonitorAsync(
        HttpClient client,
        Guid endpointId,
        bool enabled,
        int timeoutMs = 2_000,
        int expectedStatusCode = 200)
    {
        using var response = await client.PostAsJsonAsync(
            $"/endpoints/{endpointId}/monitors",
            ValidMonitorBody(timeoutMs, expectedStatusCode, enabled: enabled));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadObjectAsync(response)).GetProperty("id").GetGuid();
    }

    private static object ValidMonitorBody(
        int timeoutMs = 2_000,
        int expectedStatusCode = 200,
        int intervalSeconds = 60,
        bool enabled = true) => new
        {
            timeoutMs,
            expectedStatusCode,
            maxLatencyMs = (int?)null,
            intervalSeconds,
            enabled,
            ignoredPaths = Array.Empty<string>()
        };

    private static async Task<JsonElement[]> WaitForRunCountAsync(
        HttpClient client,
        Guid monitorId,
        int minimumCount)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeoutAt)
        {
            var runs = await GetRunsAsync(client, monitorId);
            if (runs.Length >= minimumCount)
            {
                return runs;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(
            $"O monitor {monitorId} não atingiu {minimumCount} execuções automáticas.");
    }

    private static async Task<JsonElement[]> GetRunsAsync(HttpClient client, Guid monitorId)
    {
        using var response = await client.GetAsync($"/monitors/{monitorId}/runs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadArrayAsync(response);
    }

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

internal sealed class ScheduledApiSentinelWebApplicationFactory : ApiSentinelWebApplicationFactory
{
    protected override void ConfigureAdditionalServices(IServiceCollection services)
    {
        services.RemoveAll<IMonitorScheduleManager>();
        services.AddSingleton<TestMonitorScheduleManager>();
        services.AddSingleton<IMonitorScheduleManager>(provider =>
            provider.GetRequiredService<TestMonitorScheduleManager>());
    }
}

internal sealed class TestMonitorScheduleManager(
    IServiceScopeFactory scopeFactory) : IMonitorScheduleManager, IAsyncDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(150);
    private readonly ConcurrentDictionary<Guid, ScheduleLoop> _schedules = [];

    public bool HasSchedule(Guid monitorId) => _schedules.ContainsKey(monitorId);

    public async Task UpsertAsync(
        MonitorSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        await RemoveAsync(schedule.MonitorId, cancellationToken);
        var cancellationSource = new CancellationTokenSource();
        var loop = new ScheduleLoop(
            cancellationSource,
            RunAsync(schedule.MonitorId, cancellationSource.Token));
        _schedules[schedule.MonitorId] = loop;
    }

    public async Task RemoveAsync(
        Guid monitorId,
        CancellationToken cancellationToken = default)
    {
        if (!_schedules.TryRemove(monitorId, out var loop))
        {
            return;
        }

        await loop.CancellationSource.CancelAsync();
        try
        {
            await loop.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (loop.CancellationSource.IsCancellationRequested)
        {
        }
        finally
        {
            loop.CancellationSource.Dispose();
        }
    }

    public async Task ReconcileAsync(
        IReadOnlyCollection<MonitorSchedule> schedules,
        CancellationToken cancellationToken = default)
    {
        var expectedIds = schedules.Select(schedule => schedule.MonitorId).ToHashSet();
        foreach (var monitorId in _schedules.Keys.Where(id => !expectedIds.Contains(id)))
        {
            await RemoveAsync(monitorId, cancellationToken);
        }

        foreach (var schedule in schedules)
        {
            await UpsertAsync(schedule, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var monitorId in _schedules.Keys)
        {
            await RemoveAsync(monitorId);
        }
    }

    private async Task RunAsync(Guid monitorId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var job = scope.ServiceProvider.GetRequiredService<IScheduledMonitorJob>();
                await job.ExecuteAsync(monitorId, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private sealed record ScheduleLoop(CancellationTokenSource CancellationSource, Task Task);
}
