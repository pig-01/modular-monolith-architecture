using DataSource.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Order.Infrastructure;
using Product.Infrastructure;
using User.Infrastructure;
using ProductEntity = Product.Domain.Entities.Product;
using UserEntity = User.Domain.Entities.User;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? "Data Source=localhost;Initial Catalog=ModularMonolithDemo;Integrated Security=SSPI;TrustServerCertificate=true;app=LINQPad";

await EnsureUserDataAsync(connectionString);
await EnsureProductDataAsync(connectionString);
await EnsureOrderDatabaseAsync(connectionString);

await using var dataSources = new DataSourceDbContext(new DbContextOptionsBuilder<DataSourceDbContext>()
    .UseSqlServer(connectionString).Options);
await dataSources.Database.MigrateAsync();

static async Task EnsureUserDataAsync(string connectionString)
{
    var options = new DbContextOptionsBuilder<UserDbContext>()
        .UseSqlServer(connectionString)
        .Options;

    await using UserDbContext context = new(options);
    await context.Database.MigrateAsync();

    if (await context.Users.AnyAsync())
    {
        return;
    }

    List<UserEntity> users = Enumerable.Range(1, 10)
        .Select(i => new UserEntity(Guid.NewGuid(), $"User {i}", $"user{i}@example.com"))
        .ToList();

    await context.Users.AddRangeAsync(users);
    await context.SaveChangesAsync();
}

static async Task EnsureProductDataAsync(string connectionString)
{
    var options = new DbContextOptionsBuilder<ProductDbContext>()
        .UseSqlServer(connectionString)
        .Options;

    await using ProductDbContext context = new(options);
    await context.Database.MigrateAsync();

    if (await context.Products.AnyAsync())
    {
        return;
    }

    List<ProductEntity> products = Enumerable.Range(1, 30)
        .Select(i => new ProductEntity(Guid.NewGuid(), $"Product {i}", 10 + i))
        .ToList();

    await context.Products.AddRangeAsync(products);
    await context.SaveChangesAsync();
}

static async Task EnsureOrderDatabaseAsync(string connectionString)
{
    var options = new DbContextOptionsBuilder<OrderDbContext>()
        .UseSqlServer(connectionString)
        .Options;

    await using OrderDbContext context = new(options);
    await context.Database.MigrateAsync();
}
