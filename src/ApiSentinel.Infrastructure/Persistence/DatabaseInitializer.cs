using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApiSentinel.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private const int MaxMigrationAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    public static async Task ApplyDatabaseMigrationsAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseInitializer));
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();

        if (environment.IsEnvironment("Testing") && !context.Database.IsSqlServer())
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);
            logger.LogInformation(
                "Database schema created from the EF Core model for the non-SQL Server test provider.");
            return;
        }

        for (var attempt = 1; attempt <= MaxMigrationAttempts; attempt++)
        {
            try
            {
                await context.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database migrations applied successfully.");
                return;
            }
            catch (SqlException exception) when (attempt < MaxMigrationAttempts)
            {
                logger.LogWarning(
                    exception,
                    "Database unavailable while applying migrations. Retrying in {DelaySeconds} seconds ({Attempt}/{MaxAttempts}).",
                    RetryDelay.TotalSeconds,
                    attempt,
                    MaxMigrationAttempts);

                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}
