using ApiSentinel.Infrastructure.Persistence;
using ApiSentinel.Modules.Monitoring.HttpExecution;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApiSentinel.Api.IntegrationTests;

public sealed class ApiSentinelWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["Hangfire:Enabled"] = "false",
                ["Monitoring:NetworkSecurity:DevelopmentInternalHosts:0"] = "mock-api-1",
                ["Monitoring:NetworkSecurity:DevelopmentInternalHosts:1"] = "mock-api-2"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDnsAddressResolver>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
            services.AddSingleton<IDnsAddressResolver, TestDnsAddressResolver>();
        });
    }

    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true
    });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

internal sealed class TestDnsAddressResolver : IDnsAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (host.Equals("mock-api-1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("mock-api-2", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new[] { IPAddress.Loopback });
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return Task.FromResult(new[] { address });
        }

        return Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}
