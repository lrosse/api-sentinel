using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiSentinel.Infrastructure.Persistence;
using ApiSentinel.Modules.Monitoring.ContractAnalysis;
using ApiSentinel.Modules.Monitoring.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApiSentinel.Api.IntegrationTests;

public sealed class Marco4IntegrationTests :
    IClassFixture<ApiSentinelWebApplicationFactory>,
    IAsyncLifetime
{
    private const string ValidPassword = "Sentinel#2026";
    private const string ContractV1 =
        "[{\"id\":1,\"nome\":\"Teclado\",\"preco\":349.90}]";
    private const string ContractV2 =
        "[{\"id\":1,\"nome\":\"Teclado\",\"preco\":349.90,\"categoria\":\"Periféricos\"}]";
    private const string ContractV3 =
        "[{\"id\":\"1\",\"preco\":349.90,\"categoria\":\"Periféricos\"}]";

    private readonly ApiSentinelWebApplicationFactory _factory;
    private readonly LocalHttpMockServer _mockServer = new();
    private string _responseBody = ContractV1;

    public Marco4IntegrationTests(ApiSentinelWebApplicationFactory factory)
    {
        _factory = factory;
        _mockServer.JsonBodyFactory = _ => Volatile.Read(ref _responseBody);
    }

    public Task InitializeAsync() => _mockServer.StartAsync();

    public Task DisposeAsync() => _mockServer.DisposeAsync().AsTask();

    [Fact]
    public void Added_field_is_compatible()
    {
        var result = Compare("{\"id\":1}", "{\"id\":1,\"categoria\":\"x\"}");

        Assert.Equal(ContractChangeClassification.Compatible, result.Classification);
        var change = Assert.Single(result.Changes);
        Assert.Equal("categoria", change.Path);
        Assert.Equal(ContractChangeType.Added, change.ChangeType);
        Assert.Null(change.OldType);
        Assert.Equal("String", change.NewType);
    }

    [Fact]
    public void Removed_field_is_breaking()
    {
        var result = Compare("{\"id\":1,\"nome\":\"x\"}", "{\"id\":1}");

        Assert.Equal(ContractChangeClassification.Breaking, result.Classification);
        var change = Assert.Single(result.Changes);
        Assert.Equal("nome", change.Path);
        Assert.Equal(ContractChangeType.Removed, change.ChangeType);
    }

    [Fact]
    public void Changed_field_type_is_breaking()
    {
        var result = Compare("{\"id\":1}", "{\"id\":\"1\"}");

        Assert.Equal(ContractChangeClassification.Breaking, result.Classification);
        var change = Assert.Single(result.Changes);
        Assert.Equal("id", change.Path);
        Assert.Equal(ContractChangeType.TypeChanged, change.ChangeType);
        Assert.Equal("Number", change.OldType);
        Assert.Equal("String", change.NewType);
    }

    [Fact]
    public void Nested_change_uses_the_complete_path()
    {
        var result = Compare(
            "{\"cliente\":{\"endereco\":{\"cidade\":\"Recife\"}}}",
            "{\"cliente\":{\"endereco\":{\"cidade\":42}}}");

        var change = Assert.Single(result.Changes);
        Assert.Equal("cliente.endereco.cidade", change.Path);
        Assert.Equal(ContractChangeType.TypeChanged, change.ChangeType);
    }

    [Fact]
    public void Array_of_objects_uses_the_first_element_shape_without_indexes()
    {
        var result = Compare(
            "[{\"id\":1},{\"id\":2,\"ignoradoPorSerSegundo\":true}]",
            "[{\"id\":1,\"categoria\":\"x\"},{\"id\":2}]");

        var change = Assert.Single(result.Changes);
        Assert.Equal("categoria", change.Path);
        Assert.Equal(ContractChangeType.Added, change.ChangeType);
    }

    [Fact]
    public async Task Identical_runs_create_snapshots_without_contract_change_and_mode_drift_is_classified()
    {
        using var owner = await RegisterAndLoginAsync();
        var monitorId = await CreateMonitorAsync(owner);

        await RunSuccessfullyAsync(owner, monitorId); // CONTRACT_MODE v1 baseline
        await RunSuccessfullyAsync(owner, monitorId); // identical v1
        Assert.Empty(await GetContractChangesAsync(owner, monitorId));

        Volatile.Write(ref _responseBody, ContractV2);
        await RunSuccessfullyAsync(owner, monitorId);
        var afterV2 = await GetContractChangesAsync(owner, monitorId);
        var compatible = Assert.Single(afterV2);
        Assert.Equal("Compatible", compatible.GetProperty("classification").GetString());
        var added = Assert.Single(compatible.GetProperty("changes").EnumerateArray());
        Assert.Equal("categoria", added.GetProperty("path").GetString());
        Assert.Equal("Added", added.GetProperty("changeType").GetString());

        Volatile.Write(ref _responseBody, ContractV3);
        await RunSuccessfullyAsync(owner, monitorId);
        var afterV3 = await GetContractChangesAsync(owner, monitorId);
        Assert.Equal(2, afterV3.Length);
        var breaking = afterV3[0];
        Assert.Equal("Breaking", breaking.GetProperty("classification").GetString());
        var breakingChanges = breaking.GetProperty("changes")
            .EnumerateArray()
            .ToDictionary(change => change.GetProperty("path").GetString()!);
        Assert.Equal("TypeChanged", breakingChanges["id"].GetProperty("changeType").GetString());
        Assert.Equal("Removed", breakingChanges["nome"].GetProperty("changeType").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await dbContext.SchemaSnapshots.CountAsync(
            snapshot => snapshot.MonitorId == monitorId));
        Assert.Equal(2, await dbContext.ContractChanges.CountAsync(
            change => change.MonitorId == monitorId));

        using var otherUser = await RegisterAndLoginAsync();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherUser.GetAsync($"/monitors/{monitorId}/contract-changes")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherUser.GetAsync($"/monitors/{monitorId}/schema-snapshot/latest")).StatusCode);
    }

    [Fact]
    public async Task Ignored_path_is_excluded_and_does_not_create_contract_change()
    {
        using var owner = await RegisterAndLoginAsync();
        Volatile.Write(ref _responseBody, "{\"id\":1,\"metadata\":{\"timestamp\":\"antes\"}}");
        var monitorId = await CreateMonitorAsync(owner, ["metadata.timestamp"]);
        await RunSuccessfullyAsync(owner, monitorId);

        Volatile.Write(ref _responseBody, "{\"id\":1,\"metadata\":{\"timestamp\":42}}");
        await RunSuccessfullyAsync(owner, monitorId);

        Assert.Empty(await GetContractChangesAsync(owner, monitorId));
        using var latestResponse = await owner.GetAsync($"/monitors/{monitorId}/schema-snapshot/latest");
        Assert.Equal(HttpStatusCode.OK, latestResponse.StatusCode);
        var latest = await ReadObjectAsync(latestResponse);
        Assert.DoesNotContain(
            latest.GetProperty("structure").EnumerateArray(),
            field => field.GetProperty("path").GetString() == "metadata.timestamp");
    }

    [Fact]
    public async Task Structure_over_the_field_limit_is_persisted_as_too_complex_without_failing_run()
    {
        using var owner = await RegisterAndLoginAsync();
        var oversized = Enumerable.Range(0, 501)
            .ToDictionary(index => $"campo{index:D3}", index => index);
        Volatile.Write(ref _responseBody, JsonSerializer.Serialize(oversized));
        var monitorId = await CreateMonitorAsync(owner);

        await RunSuccessfullyAsync(owner, monitorId);

        using var response = await owner.GetAsync($"/monitors/{monitorId}/schema-snapshot/latest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await ReadObjectAsync(response);
        Assert.Equal("TooComplex", snapshot.GetProperty("analysisStatus").GetString());
        Assert.Empty(await GetContractChangesAsync(owner, monitorId));
    }

    [Fact]
    public void Structure_over_the_depth_limit_is_marked_too_complex()
    {
        var json = "1";
        for (var level = 0; level < 11; level++)
        {
            json = $"{{\"nivel{level}\":{json}}}";
        }

        var result = CreateAnalyzer().Analyze(json, []);

        Assert.Equal(SchemaAnalysisStatus.TooComplex, result.Status);
    }

    [Fact]
    public void Deeply_nested_array_is_bounded_without_recursive_type_analysis()
    {
        var json = "1";
        for (var level = 0; level < 11; level++)
        {
            json = $"[{json}]";
        }

        var result = CreateAnalyzer().Analyze(json, []);

        Assert.Equal(SchemaAnalysisStatus.TooComplex, result.Status);
    }

    private static ContractComparisonResult Compare(
        string previousJson,
        string currentJson,
        IReadOnlyCollection<string>? ignoredPaths = null)
    {
        var ignored = ignoredPaths ?? [];
        var analyzer = CreateAnalyzer();
        var previous = analyzer.Analyze(previousJson, ignored);
        var current = analyzer.Analyze(currentJson, ignored);
        return new ContractSchemaComparer().Compare(
            previous.StructureJson,
            current.StructureJson,
            ignored);
    }

    private static SchemaStructureAnalyzer CreateAnalyzer() => new(
        Options.Create(new ContractAnalysisOptions
        {
            MaxDepth = 10,
            MaxFields = 500
        }));

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

    private async Task<Guid> CreateMonitorAsync(
        HttpClient client,
        string[]? ignoredPaths = null)
    {
        using var serviceResponse = await client.PostAsJsonAsync(
            "/api-services",
            new
            {
                name = $"API Marco 4 {Guid.NewGuid():N}",
                description = "Deriva controlada do contrato",
                tags = new[] { "contract" },
                baseUrl = $"http://mock-api-1:{_mockServer.Port}"
            });
        Assert.Equal(HttpStatusCode.Created, serviceResponse.StatusCode);
        var serviceId = (await ReadObjectAsync(serviceResponse)).GetProperty("id").GetGuid();

        using var endpointResponse = await client.PostAsJsonAsync(
            $"/api-services/{serviceId}/endpoints",
            new { path = "/produtos", method = "GET" });
        Assert.Equal(HttpStatusCode.Created, endpointResponse.StatusCode);
        var endpointId = (await ReadObjectAsync(endpointResponse)).GetProperty("id").GetGuid();

        using var monitorResponse = await client.PostAsJsonAsync(
            $"/endpoints/{endpointId}/monitors",
            new
            {
                timeoutMs = 5_000,
                expectedStatusCode = 200,
                maxLatencyMs = (int?)null,
                intervalSeconds = 300,
                enabled = false,
                ignoredPaths = ignoredPaths ?? []
            });
        Assert.Equal(HttpStatusCode.Created, monitorResponse.StatusCode);
        return (await ReadObjectAsync(monitorResponse)).GetProperty("id").GetGuid();
    }

    private static async Task RunSuccessfullyAsync(HttpClient client, Guid monitorId)
    {
        using var response = await client.PostAsync($"/monitors/{monitorId}/run", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await ReadObjectAsync(response);
        Assert.Equal("Success", run.GetProperty("status").GetString());
    }

    private static async Task<JsonElement[]> GetContractChangesAsync(
        HttpClient client,
        Guid monitorId)
    {
        using var response = await client.GetAsync($"/monitors/{monitorId}/contract-changes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static async Task<JsonElement> ReadObjectAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
