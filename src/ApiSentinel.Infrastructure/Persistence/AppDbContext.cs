using ApiSentinel.Modules.ApiCatalog;
using ApiSentinel.Modules.ApiCatalog.Domain;
using ApiSentinel.Modules.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ApiEndpoint = ApiSentinel.Modules.ApiCatalog.Domain.Endpoint;

namespace ApiSentinel.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IApiCatalogDbContext
{
    public DbSet<ApiService> ApiServices => Set<ApiService>();
    public DbSet<ApiEndpoint> Endpoints => Set<ApiEndpoint>();

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
    }
}
