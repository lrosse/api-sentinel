using ApiSentinel.Modules.ApiCatalog.Domain;
using ApiSentinel.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MonitorEntity = ApiSentinel.Modules.Monitoring.Domain.Monitor;

namespace ApiSentinel.Infrastructure.Persistence;

public static class DevelopmentDataSeeder
{
    public const string DevelopmentEmail = "dev@apisentinel.local";
    public const string DevelopmentPassword = "DevSentinel#2026";
    private const int MonitorIntervalSeconds = 60;

    private static readonly SeedApiDefinition[] ApiDefinitions =
    [
        new("Mock API 1", "http://mock-api-1:8080"),
        new("Mock API 2", "http://mock-api-2:8080")
    ];

    public static async Task SeedDevelopmentDataAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        using var scope = host.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue("SeedData:Enabled", false))
        {
            return;
        }

        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "SeedData:Enabled só pode ser usado no ambiente Development. " +
                "O seed de credenciais conhecidas é proibido em outros ambientes.");
        }

        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DevelopmentDataSeeder));
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await userManager.FindByEmailAsync(DevelopmentEmail);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = DevelopmentEmail,
                Email = DevelopmentEmail
            };
            var result = await userManager.CreateAsync(user, DevelopmentPassword);
            if (!result.Succeeded)
            {
                var errors = string.Join(
                    "; ",
                    result.Errors.Select(error => $"{error.Code}: {error.Description}"));
                throw new InvalidOperationException(
                    $"Não foi possível criar o usuário do seed de desenvolvimento: {errors}");
            }
        }

        foreach (var definition in ApiDefinitions)
        {
            var apiService = await dbContext.ApiServices.FirstOrDefaultAsync(
                candidate => candidate.OwnerUserId == user.Id &&
                             candidate.Name == definition.Name,
                cancellationToken);
            if (apiService is null)
            {
                apiService = new ApiService
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = user.Id,
                    Name = definition.Name,
                    Description = "API de exemplo criada pelo seed de desenvolvimento local.",
                    Tags = ["desenvolvimento", "mock"],
                    BaseUrl = definition.BaseUrl
                };
                dbContext.ApiServices.Add(apiService);
            }

            var endpoint = await dbContext.Endpoints.FirstOrDefaultAsync(
                candidate => candidate.ApiServiceId == apiService.Id &&
                             candidate.Path == "/produtos" &&
                             candidate.Method == EndpointMethod.GET,
                cancellationToken);
            if (endpoint is null)
            {
                endpoint = new Endpoint
                {
                    Id = Guid.NewGuid(),
                    ApiServiceId = apiService.Id,
                    ApiService = apiService,
                    Path = "/produtos",
                    Method = EndpointMethod.GET
                };
                dbContext.Endpoints.Add(endpoint);
            }

            var hasMonitor = await dbContext.Monitors.AnyAsync(
                candidate => candidate.EndpointId == endpoint.Id,
                cancellationToken);
            if (!hasMonitor)
            {
                dbContext.Monitors.Add(new MonitorEntity
                {
                    Id = Guid.NewGuid(),
                    EndpointId = endpoint.Id,
                    Endpoint = endpoint,
                    TimeoutMs = 5_000,
                    ExpectedStatusCode = 200,
                    MaxLatencyMs = null,
                    ConsecutiveFailuresThreshold = 3,
                    IntervalSeconds = MonitorIntervalSeconds,
                    Enabled = true,
                    IgnoredPaths = []
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Development seed ensured user {DevelopmentUser} and {ApiCount} mock APIs.",
            DevelopmentEmail,
            ApiDefinitions.Length);
    }

    private sealed record SeedApiDefinition(string Name, string BaseUrl);
}
