using ApiSentinel.Modules.ApiCatalog.Domain;
using ApiSentinel.Modules.Monitoring.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApiSentinel.Modules.Monitoring;

public interface IMonitoringDbContext
{
    DbSet<ApiService> ApiServices { get; }
    DbSet<Endpoint> Endpoints { get; }
    DbSet<Domain.Monitor> Monitors { get; }
    DbSet<CheckRun> CheckRuns { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
