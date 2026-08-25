using ApiSentinel.Modules.Incidents.Domain;
using ApiSentinel.Modules.Monitoring.Domain;
using Microsoft.EntityFrameworkCore;
using MonitorEntity = ApiSentinel.Modules.Monitoring.Domain.Monitor;

namespace ApiSentinel.Modules.Incidents;

public interface IIncidentsDbContext
{
    DbSet<MonitorEntity> Monitors { get; }
    DbSet<CheckRun> CheckRuns { get; }
    DbSet<ContractChange> ContractChanges { get; }
    DbSet<Incident> Incidents { get; }
    DbSet<IncidentEvent> IncidentEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
