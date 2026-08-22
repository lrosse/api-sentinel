using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ApiSentinel.Api.IntegrationTests;

public sealed class Marco1IntegrationTests(
    ApiSentinelWebApplicationFactory factory)
    : IClassFixture<ApiSentinelWebApplicationFactory>
{
    private const string ValidPassword = "Sentinel#2026";

    public static TheoryData<HttpMethod, string, object?> ProtectedCatalogRequests => new()
    {
        { HttpMethod.Post, "/api-services", ValidApiServiceBody() },
        { HttpMethod.Get, "/api-services", null },
        { HttpMethod.Get, $"/api-services/{Guid.NewGuid()}", null },
        { HttpMethod.Put, $"/api-services/{Guid.NewGuid()}", ValidApiServiceBody() },
        { HttpMethod.Delete, $"/api-services/{Guid.NewGuid()}", null },
        { HttpMethod.Post, $"/api-services/{Guid.NewGuid()}/endpoints", ValidEndpointBody() },
        { HttpMethod.Get, $"/api-services/{Guid.NewGuid()}/endpoints", null },
        { HttpMethod.Get, $"/endpoints/{Guid.NewGuid()}", null },
        { HttpMethod.Put, $"/endpoints/{Guid.NewGuid()}", ValidEndpointBody() },
        { HttpMethod.Delete, $"/endpoints/{Guid.NewGuid()}", null }
    };

    [Theory]
    [MemberData(nameof(ProtectedCatalogRequests))]
    public async Task Catalog_endpoints_require_authentication(
        HttpMethod method,
        string path,
        object? body)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiService_requires_a_valid_http_or_https_base_url()
    {
        using var client = await RegisterAndLoginAsync();

        using var missingResponse = await client.PostAsJsonAsync(
            "/api-services",
            new { name = "Sem URL", description = "inválido", tags = Array.Empty<string>() });
        using var invalidResponse = await client.PostAsJsonAsync(
            "/api-services",
            new { name = "URL inválida", baseUrl = "ftp://example.com" });
        using var invalidMethodResponse = await client.PostAsJsonAsync(
            $"/api-services/{Guid.NewGuid()}/endpoints",
            new { path = "/products", method = 99 });

        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidMethodResponse.StatusCode);
        Assert.Contains("baseUrl", await missingResponse.Content.ReadAsStringAsync());
        Assert.Contains("baseUrl", await invalidResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Users_cannot_see_edit_or_delete_another_users_catalog()
    {
        using var owner = await RegisterAndLoginAsync();
        using var otherUser = await RegisterAndLoginAsync();

        var apiServiceId = await CreateApiServiceAsync(owner);
        var endpointId = await CreateEndpointAsync(owner, apiServiceId);

        using var listResponse = await otherUser.GetAsync("/api-services");
        using var getResponse = await otherUser.GetAsync($"/api-services/{apiServiceId}");
        using var updateResponse = await otherUser.PutAsJsonAsync(
            $"/api-services/{apiServiceId}",
            ValidApiServiceBody("Tentativa de alteração"));
        using var deleteResponse = await otherUser.DeleteAsync($"/api-services/{apiServiceId}");
        using var nestedListResponse = await otherUser.GetAsync(
            $"/api-services/{apiServiceId}/endpoints");
        using var endpointUpdateResponse = await otherUser.PutAsJsonAsync(
            $"/endpoints/{endpointId}",
            ValidEndpointBody("/roubado"));
        using var endpointDeleteResponse = await otherUser.DeleteAsync($"/endpoints/{endpointId}");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Empty(await ReadArrayAsync(listResponse));
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nestedListResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, endpointUpdateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, endpointDeleteResponse.StatusCode);

        using var ownerStillHasService = await owner.GetAsync($"/api-services/{apiServiceId}");
        using var ownerStillHasEndpoint = await owner.GetAsync(
            $"/api-services/{apiServiceId}/endpoints");
        Assert.Equal(HttpStatusCode.OK, ownerStillHasService.StatusCode);
        Assert.Single(await ReadArrayAsync(ownerStillHasEndpoint));
    }

    [Fact]
    public async Task Complete_catalog_flow_works_through_http()
    {
        using var client = await RegisterAndLoginAsync();

        var apiServiceId = await CreateApiServiceAsync(client);
        var endpointId = await CreateEndpointAsync(client, apiServiceId);

        using var serviceListResponse = await client.GetAsync("/api-services");
        using var endpointListResponse = await client.GetAsync(
            $"/api-services/{apiServiceId}/endpoints");
        Assert.Single(await ReadArrayAsync(serviceListResponse));
        Assert.Single(await ReadArrayAsync(endpointListResponse));

        using var updateServiceResponse = await client.PutAsJsonAsync(
            $"/api-services/{apiServiceId}",
            ValidApiServiceBody("Catálogo atualizado"));
        using var updateEndpointResponse = await client.PutAsJsonAsync(
            $"/endpoints/{endpointId}",
            ValidEndpointBody("/products/{id}"));
        Assert.Equal(HttpStatusCode.OK, updateServiceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updateEndpointResponse.StatusCode);

        using var updatedService = JsonDocument.Parse(
            await updateServiceResponse.Content.ReadAsStringAsync());
        using var updatedEndpoint = JsonDocument.Parse(
            await updateEndpointResponse.Content.ReadAsStringAsync());
        Assert.Equal("Catálogo atualizado", updatedService.RootElement.GetProperty("name").GetString());
        Assert.Equal("/products/{id}", updatedEndpoint.RootElement.GetProperty("path").GetString());

        using var deleteEndpointResponse = await client.DeleteAsync($"/endpoints/{endpointId}");
        using var deleteServiceResponse = await client.DeleteAsync($"/api-services/{apiServiceId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteEndpointResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleteServiceResponse.StatusCode);

        using var deletedServiceResponse = await client.GetAsync($"/api-services/{apiServiceId}");
        Assert.Equal(HttpStatusCode.NotFound, deletedServiceResponse.StatusCode);

        using var logoutResponse = await client.PostAsync("/auth/logout", null);
        using var meAfterLogoutResponse = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogoutResponse.StatusCode);
    }

    private async Task<HttpClient> RegisterAndLoginAsync()
    {
        var client = factory.CreateApiClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";

        using var registerResponse = await client.PostAsJsonAsync(
            "/auth/register",
            new { email, password = ValidPassword });
        using var loginResponse = await client.PostAsJsonAsync(
            "/auth/login",
            new { email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var authCookie = Assert.Single(
            loginResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("ApiSentinel.Auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", authCookie, StringComparison.OrdinalIgnoreCase);

        using var meResponse = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        return client;
    }

    private static async Task<Guid> CreateApiServiceAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api-services", ValidApiServiceBody());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadIdAsync(response);
    }

    private static async Task<Guid> CreateEndpointAsync(HttpClient client, Guid apiServiceId)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api-services/{apiServiceId}/endpoints",
            ValidEndpointBody());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadIdAsync(response);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement.ArrayEnumerator> ReadArrayAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone().EnumerateArray();
    }

    private static object ValidApiServiceBody(string name = "Produtos") => new
    {
        name,
        description = "API de produtos",
        tags = new[] { "catalog", "demo" },
        baseUrl = "https://api.example.com"
    };

    private static object ValidEndpointBody(string path = "/products") => new
    {
        path,
        method = "GET"
    };
}
