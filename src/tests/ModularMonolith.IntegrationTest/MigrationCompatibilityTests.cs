using DataSource.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Order.Infrastructure;
using Product.Infrastructure;
using User.Infrastructure;

namespace ModularMonolith.IntegrationTest;

public class MigrationCompatibilityTests
{
    [Fact]
    public void UpgradedProviders_MatchExistingMigrationSnapshots()
    {
        const string connectionString = "Server=localhost;Database=MigrationTests;Integrated Security=true;TrustServerCertificate=true";
        using UserDbContext users = new(new DbContextOptionsBuilder<UserDbContext>()
            .UseSqlServer(connectionString).Options);
        using OrderDbContext orders = new(new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer(connectionString).Options);
        using ProductDbContext products = new(new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer(connectionString).Options);
        using DataSourceDbContext dataSources = new(new DbContextOptionsBuilder<DataSourceDbContext>()
            .UseSqlServer(connectionString).Options);

        Assert.False(users.Database.HasPendingModelChanges());
        Assert.False(orders.Database.HasPendingModelChanges());
        Assert.False(products.Database.HasPendingModelChanges());
        Assert.NotEmpty(dataSources.Database.GetMigrations());
        Assert.False(dataSources.Database.HasPendingModelChanges());
    }
}
