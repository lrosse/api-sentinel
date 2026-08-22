using ApiSentinel.Modules.ApiCatalog;
using ApiSentinel.Modules.ApiCatalog.Domain;
using ApiSentinel.Modules.Identity;
using ApiSentinel.Modules.Monitoring;
using ApiSentinel.Modules.Monitoring.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ApiEndpoint = ApiSentinel.Modules.ApiCatalog.Domain.Endpoint;
using MonitorEntity = ApiSentinel.Modules.Monitoring.Domain.Monitor;

namespace ApiSentinel.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IApiCatalogDbContext, IMonitoringDbContext
{
    public DbSet<ApiService> ApiServices => Set<ApiService>();
    public DbSet<ApiEndpoint> Endpoints => Set<ApiEndpoint>();
    public DbSet<MonitorEntity> Monitors => Set<MonitorEntity>();
    public DbSet<CheckRun> CheckRuns => Set<CheckRun>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApiService>(entity =>
        {
            entity.ToTable("ApiServices");
            entity.HasKey(apiService => apiService.Id);
            entity.Property(apiService => apiService.OwnerUserId)
                .HasMaxLength(450)
                .IsRequired();
            entity.Property(apiService => apiService.Name)
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(apiService => apiService.Description)
                .HasMaxLength(1_000);
            entity.Property(apiService => apiService.Tags)
                .IsRequired();
            entity.Property(apiService => apiService.BaseUrl)
                .HasMaxLength(2_048)
                .IsRequired();
            entity.HasIndex(apiService => apiService.OwnerUserId);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(apiService => apiService.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApiEndpoint>(entity =>
        {
            entity.ToTable("Endpoints");
            entity.HasKey(endpoint => endpoint.Id);
            entity.Property(endpoint => endpoint.Path)
                .HasMaxLength(2_048)
                .IsRequired();
            entity.Property(endpoint => endpoint.Method)
                .HasConversion<string>()
                .HasMaxLength(10)
                .IsRequired();
            entity.HasIndex(endpoint => endpoint.ApiServiceId);
            entity.HasOne(endpoint => endpoint.ApiService)
                .WithMany(apiService => apiService.Endpoints)
                .HasForeignKey(endpoint => endpoint.ApiServiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MonitorEntity>(entity =>
        {
            entity.ToTable("Monitors");
            entity.HasKey(monitor => monitor.Id);
            entity.Property(monitor => monitor.TimeoutMs).IsRequired();
            entity.Property(monitor => monitor.ExpectedStatusCode).IsRequired();
            entity.Property(monitor => monitor.IgnoredPaths).IsRequired();
            entity.HasIndex(monitor => monitor.EndpointId);
            entity.HasOne(monitor => monitor.Endpoint)
                .WithMany()
                .HasForeignKey(monitor => monitor.EndpointId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CheckRun>(entity =>
        {
            entity.ToTable("CheckRuns");
            entity.HasKey(run => run.Id);
            entity.Property(run => run.StartedAt).IsRequired();
            entity.Property(run => run.FinishedAt).IsRequired();
            entity.Property(run => run.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(run => run.LatencyMs).IsRequired();
            entity.Property(run => run.ErrorMessage).HasMaxLength(1_000);
            entity.Property(run => run.ResponseBodySnippet).HasMaxLength(4_096);
            entity.HasIndex(run => new { run.MonitorId, run.StartedAt });
            entity.HasOne(run => run.Monitor)
                .WithMany(monitor => monitor.CheckRuns)
                .HasForeignKey(run => run.MonitorId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
