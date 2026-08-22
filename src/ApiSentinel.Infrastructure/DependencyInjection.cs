using ApiSentinel.Infrastructure.Persistence;
using ApiSentinel.Infrastructure.Scheduling;
using ApiSentinel.Modules.ApiCatalog;
using ApiSentinel.Modules.Identity;
using ApiSentinel.Modules.Monitoring;
using ApiSentinel.Modules.Monitoring.Scheduling;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApiSentinel.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "A connection string 'DefaultConnection' não foi configurada.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddIdentityModule()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped<IApiCatalogDbContext>(provider =>
            provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IMonitoringDbContext>(provider =>
            provider.GetRequiredService<AppDbContext>());

        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName("ApiSentinel");

        var dataProtectionKeysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
        }

        if (!environment.IsEnvironment("Testing") &&
            configuration.GetValue("Hangfire:Enabled", true))
        {
            services.AddHangfire(globalConfiguration => globalConfiguration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                {
                    PrepareSchemaIfNecessary = true,
                    QueuePollInterval = TimeSpan.FromSeconds(15)
                }));

            services.AddHangfireServer();
            services.AddSingleton<IMonitorScheduleManager, HangfireMonitorScheduleManager>();
        }

        return services;
    }
}
