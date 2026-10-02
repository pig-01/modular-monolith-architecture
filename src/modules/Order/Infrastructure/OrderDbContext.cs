using Microsoft.EntityFrameworkCore;
using Order.Domain.Entities;

namespace Order.Infrastructure;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

    public DbSet<Order.Domain.Entities.Order> Orders => Set<Order.Domain.Entities.Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order.Domain.Entities.Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).IsRequired();
            entity.Ignore(x => x.DomainEvents);
            entity.HasMany(x => x.Items).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems");
            entity.Property<Guid>("_Id").HasColumnName("Id").ValueGeneratedOnAdd();
            entity.HasKey("_Id");
            entity.Property(x => x.ProductId).IsRequired();
            entity.Property(x => x.Quantity).IsRequired();
            entity.Property(x => x.Price).HasColumnType("decimal(18,2)").IsRequired();
        });
    }
}