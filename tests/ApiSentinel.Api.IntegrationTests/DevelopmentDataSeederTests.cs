using ApiSentinel.Infrastructure.Persistence;
using ApiSentinel.Modules.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ApiSentinel.Api.IntegrationTests;

public sealed class DevelopmentDataSeederTests
{
    [Fact]
    public async Task Enabled_seed_populates_an_empty_development_database_idempotently()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var host = CreateHost(connection, Environments.Development, seedEnabled: true);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        await host.SeedDevelopmentDataAsync();
        await host.SeedDevelopmentDataAsync();

        await using var verificationScope = host.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = verificationScope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(DevelopmentDataSeeder.DevelopmentEmail);

        Assert.NotNull(user);
        Assert.True(await userManager.CheckPasswordAsync(
            user,
            DevelopmentDataSeeder.DevelopmentPassword));

        var apiServices = await verificationDb.ApiServices
            .Where(apiService => apiService.OwnerUserId == user.Id)
            .OrderBy(apiService => apiService.Name)
            .ToListAsync();
        Assert.Collection(
            apiServices,
            first =>
            {
                Assert.Equal("Mock API 1", first.Name);
                Assert.Equal("http://mock-api-1:8080", first.BaseUrl);
            },
            second =>
            {
                Assert.Equal("Mock API 2", second.Name);
                Assert.Equal("http://mock-api-2:8080", second.BaseUrl);
            });

        var endpoints = await verificationDb.Endpoints
            .Where(endpoint => apiServices.Select(apiService => apiService.Id)
                .Contains(endpoint.ApiServiceId))
            .ToListAsync();
        Assert.Equal(2, endpoints.Count);
        Assert.All(endpoints, endpoint =>
        {
            Assert.Equal("/produtos", endpoint.Path);
            Assert.Equal("GET", endpoint.Method.ToString());
        });

        var monitors = await verificationDb.Monitors
            .Where(monitor => endpoints.Select(endpoint => endpoint.Id)
                .Contains(monitor.EndpointId))
            .ToListAsync();
        Assert.Equal(2, monitors.Count);
        Assert.All(monitors, monitor =>
        {
            Assert.Equal(5_000, monitor.TimeoutMs);
            Assert.Equal(200, monitor.ExpectedStatusCode);
            Assert.Equal(60, monitor.IntervalSeconds);
            Assert.True(monitor.Enabled);
        });
    }

    [Fact]
    public async Task Enabled_seed_is_explicitly_rejected_outside_development()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        using var host = CreateHost(connection, Environments.Production, seedEnabled: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.SeedDevelopmentDataAsync());

        Assert.Contains("só pode ser usado no ambiente Development", exception.Message);
    }

    private static IHost CreateHost(
        SqliteConnection connection,
        string environment,
        bool seedEnabled) =>
        Host.CreateDefaultBuilder()
            .UseEnvironment(environment)
            .ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SeedData:Enabled"] = seedEnabled.ToString()
                }))
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
                services.AddIdentityModule()
                    .AddEntityFrameworkStores<AppDbContext>();
            })
            .Build();
}
