using ApiSentinel.Modules.ApiCatalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApiSentinel.Modules.ApiCatalog;

public interface IApiCatalogDbContext
{
    DbSet<ApiService> ApiServices { get; }
    DbSet<Endpoint> Endpoints { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
