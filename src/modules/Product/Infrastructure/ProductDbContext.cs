using Microsoft.EntityFrameworkCore;
using Product.Domain.Entities;

namespace Product.Infrastructure;

public class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

    public DbSet<Product.Domain.Entities.Product> Products => Set<Product.Domain.Entities.Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Product.Domain.Entities.Product>(entity =>
                                                                               {
                                                                                   entity.ToTable("Products");
                                                                                   entity.HasKey(x => x.Id);
                                                                                   entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                                                                                   entity.Property(x => x.Price).HasColumnType("decimal(18,2)").IsRequired();
                                                                                   entity.Ignore(x => x.DomainEvents);
                                                                               });
}