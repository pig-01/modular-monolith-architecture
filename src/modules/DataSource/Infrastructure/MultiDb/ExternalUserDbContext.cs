using Microsoft.EntityFrameworkCore;

namespace DataSource.Infrastructure.MultiDb;

/// <summary>
/// A generic DbContext used exclusively for querying external data sources.
/// It defines the common schema contract (Users table) that all registered data sources must satisfy.
/// Created dynamically by MultiDbContextFactory for each provider type.
/// </summary>
public class ExternalUserDbContext : DbContext
{
    public ExternalUserDbContext(DbContextOptions options) : base(options) { }

    public DbSet<ExternalUser> Users => Set<ExternalUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<ExternalUser>(entity =>
                                                                               {
                                                                                   entity.ToTable("Users");
                                                                                   entity.HasKey(e => e.Id);
                                                                                   entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
                                                                                   entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
                                                                               });
}