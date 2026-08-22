using Microsoft.EntityFrameworkCore;

namespace ApiSentinel.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
