using System.Security.Claims;
using ApiSentinel.Modules.ApiCatalog.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ApiSentinel.Modules.ApiCatalog;

public static class ApiCatalogModule
{
    private const int MaxNameLength = 120;
    private const int MaxDescriptionLength = 1_000;
    private const int MaxUrlLength = 2_048;
    private const int MaxPathLength = 2_048;
    private const int MaxTags = 20;
    private const int MaxTagLength = 50;

    public static IEndpointRouteBuilder MapApiCatalogModule(this IEndpointRouteBuilder endpoints)
    {
        var services = endpoints
            .MapGroup("/api-services")
            .WithTags("API Catalog")
            .RequireAuthorization();

        services.MapPost("/", CreateApiServiceAsync);
        services.MapGet("/", ListApiServicesAsync);
        services.MapGet("/{id:guid}", GetApiServiceAsync);
        services.MapPut("/{id:guid}", UpdateApiServiceAsync);
        services.MapDelete("/{id:guid}", DeleteApiServiceAsync);
        services.MapPost("/{id:guid}/endpoints", CreateEndpointAsync);
        services.MapGet("/{id:guid}/endpoints", ListEndpointsAsync);

        var apiEndpoints = endpoints
            .MapGroup("/endpoints")
            .WithTags("API Catalog")
            .RequireAuthorization();

        apiEndpoints.MapPut("/{id:guid}", UpdateEndpointAsync);
        apiEndpoints.MapDelete("/{id:guid}", DeleteEndpointAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateApiServiceAsync(
        ApiServiceRequest request,
        ClaimsPrincipal principal,
        IApiCatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var errors = ValidateApiService(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var apiService = new ApiService
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Name = request.Name!.Trim(),
            Description = NormalizeOptional(request.Description),
            Tags = NormalizeTags(request.Tags),
            BaseUrl = request.BaseUrl!.Trim()
        };

        dbContext.ApiServices.Add(apiService);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api-services/{apiService.Id}",
            ToResponse(apiService));
    }

    private static async Task<IResult> ListApiServicesAsync(
        ClaimsPrincipal principal,
        IApiCatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var services = await dbContext.ApiServices
            .AsNoTracking()
            .Where(apiService => apiService.OwnerUserId == ownerUserId)
            .OrderBy(apiService => apiService.Name)
            .Select(apiService => ToResponse(apiService))
            .ToListAsync(cancellationToken);

        return Results.Ok(services);
    }

    private static async Task<IResult> GetApiServiceAsync(
        Guid id,
        ClaimsPrincipal principal,
        IApiCatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var apiService = await dbContext.ApiServices
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == id && candidate.OwnerUserId == ownerUserId,
                cancellationToken);

        return apiService is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(apiService));
    }

    private static async Task<IResult> UpdateApiServiceAsync(
        Guid id,
        ApiServiceRequest request,
        ClaimsPrincipal principal,
        IApiCatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var errors = ValidateApiService(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var apiService = await dbContext.ApiServices.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.OwnerUserId == ownerUserId,
            cancellationToken);

        if (apiService is null)
        {
            return Results.NotFound();
        }

        apiService.Name = request.Name!.Trim();
        apiService.Description = NormalizeOptional(request.Description);
        apiService.Tags = NormalizeTags(request.Tags);
        apiService.BaseUrl = request.BaseUrl!.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(apiService));
    }

    private static async Task<IResult> DeleteApiServiceAsync(
        Guid id,
        ClaimsPrincipal principal,
        IApiCatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var apiService = await dbContext.ApiServices.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.OwnerUserId == ownerUserId,
            cancellationToken);

        if (apiService is null)
        {
            return Results.NotFound();
        }

        dbContext.ApiServices.Remove(apiService);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateEndpointAsync(
        Guid id,
        EndpointRequest request,
        ClaimsPrincipal principal,
        IApiCatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var errors = ValidateEndpoint(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var apiService = await dbContext.ApiServices.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.OwnerUserId == ownerUserId,
            cancellationToken);

        if (apiService is null)
        {
            return Results.NotFound();
        }

        var endpoint = new Domain.Endpoint
        {
            Id = Guid.NewGuid(),
            ApiServiceId = apiService.Id,
            ApiService = apiService,
            Path = request.Path!.Trim(),
            Method = request.Method
        };

        dbContext.Endpoints.Add(endpoint);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/endpoints/{endpoint.Id}", ToResponse(endpoint));
    }

    private static async Task<IResult> ListEndpointsAsync(
        Guid id,
        ClaimsPrincipal principal,
        IApiCatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var ownsApiService = await dbContext.ApiServices.AnyAsync(
            candidate => candidate.Id == id && candidate.OwnerUserId == ownerUserId,
            cancellationToken);

        if (!ownsApiService)
        {
            return Results.NotFound();
        }

        var apiEndpoints = await dbContext.Endpoints
            .AsNoTracking()
            .Where(endpoint => endpoint.ApiServiceId == id)
            .OrderBy(endpoint => endpoint.Path)
            .ThenBy(endpoint => endpoint.Method)
            .Select(endpoint => ToResponse(endpoint))
            .ToListAsync(cancellationToken);

        return Results.Ok(apiEndpoints);
    }

    private static async Task<IResult> UpdateEndpointAsync(
        Guid id,
        EndpointRequest request,
        ClaimsPrincipal principal,
        IApiCatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var errors = ValidateEndpoint(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var endpoint = await dbContext.Endpoints.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.ApiService.OwnerUserId == ownerUserId,
            cancellationToken);

        if (endpoint is null)
        {
            return Results.NotFound();
        }

        endpoint.Path = request.Path!.Trim();
        endpoint.Method = request.Method;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(endpoint));
    }

    private static async Task<IResult> DeleteEndpointAsync(
        Guid id,
        ClaimsPrincipal principal,
        IApiCatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var ownerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (ownerUserId is null)
        {
            return Results.Unauthorized();
        }

        var endpoint = await dbContext.Endpoints.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.ApiService.OwnerUserId == ownerUserId,
            cancellationToken);

        if (endpoint is null)
        {
            return Results.NotFound();
        }

        dbContext.Endpoints.Remove(endpoint);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static Dictionary<string, string[]> ValidateApiService(ApiServiceRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["O nome é obrigatório."];
        }
        else if (request.Name.Trim().Length > MaxNameLength)
        {
            errors["name"] = [$"O nome deve ter no máximo {MaxNameLength} caracteres."];
        }

        if (request.Description?.Trim().Length > MaxDescriptionLength)
        {
            errors["description"] =
                [$"A descrição deve ter no máximo {MaxDescriptionLength} caracteres."];
        }

        if (string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            errors["baseUrl"] = ["A URL base é obrigatória."];
        }
        else if (request.BaseUrl.Trim().Length > MaxUrlLength ||
                 !IsValidHttpUrl(request.BaseUrl.Trim()))
        {
            errors["baseUrl"] = ["A URL base deve ser uma URL absoluta HTTP ou HTTPS válida."];
        }

        var normalizedTags = NormalizeTags(request.Tags);
        if (normalizedTags.Count > MaxTags)
        {
            errors["tags"] = [$"Informe no máximo {MaxTags} tags."];
        }
        else if (normalizedTags.Any(tag => tag.Length > MaxTagLength))
        {
            errors["tags"] = [$"Cada tag deve ter no máximo {MaxTagLength} caracteres."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateEndpoint(EndpointRequest request)
    {
        if (!Enum.IsDefined(request.Method))
        {
            return new Dictionary<string, string[]>
            {
                ["method"] = ["O método HTTP informado é inválido."]
            };
        }

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return new Dictionary<string, string[]>
            {
                ["path"] = ["O path do endpoint é obrigatório."]
            };
        }

        if (request.Path.Trim().Length > MaxPathLength)
        {
            return new Dictionary<string, string[]>
            {
                ["path"] = [$"O path deve ter no máximo {MaxPathLength} caracteres."]
            };
        }

        return [];
    }

    private static bool IsValidHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        !string.IsNullOrWhiteSpace(uri.Host) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> NormalizeTags(IEnumerable<string>? tags) =>
        tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private static ApiServiceResponse ToResponse(ApiService apiService) =>
        new(
            apiService.Id,
            apiService.Name,
            apiService.Description,
            apiService.Tags,
            apiService.BaseUrl);

    private static EndpointResponse ToResponse(Domain.Endpoint endpoint) =>
        new(endpoint.Id, endpoint.ApiServiceId, endpoint.Path, endpoint.Method);

    public sealed record ApiServiceRequest(
        string? Name,
        string? Description,
        IReadOnlyCollection<string>? Tags,
        string? BaseUrl);

    public sealed record ApiServiceResponse(
        Guid Id,
        string Name,
        string? Description,
        IReadOnlyCollection<string> Tags,
        string BaseUrl);

    public sealed record EndpointRequest(string? Path, EndpointMethod Method);

    public sealed record EndpointResponse(
        Guid Id,
        Guid ApiServiceId,
        string Path,
        EndpointMethod Method);
}
