using DataSource.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DataSource.Infrastructure;

public class DataSourceDbContext : DbContext
{
    public DataSourceDbContext(DbContextOptions<DataSourceDbContext> options) : base(options) { }

    public DbSet<DataSource.Domain.Entities.DataSource> DataSources => Set<DataSource.Domain.Entities.DataSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<DataSource.Domain.Entities.DataSource>(entity =>
                                                                               {
                                                                                   entity.ToTable("DataSources");
                                                                                   entity.HasKey(x => x.Id);
                                                                                   entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                                                                                   entity.Property(x => x.ConnectionString).IsRequired();
                                                                                   entity.Property(x => x.Provider)
                                                                                       .HasConversion<string>()
                                                                                       .HasMaxLength(50)
                                                                                       .IsRequired();
                                                                                   entity.Property(x => x.UserId).IsRequired();
                                                                                   entity.Property(x => x.CreatedAt).IsRequired();

                                                                                   entity.HasIndex(x => x.UserId);
                                                                               });
}