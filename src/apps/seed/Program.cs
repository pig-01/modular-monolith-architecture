using Auth.Application;
using Auth.Infrastructure;
using DataSource.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Order.Infrastructure;
using Product.Infrastructure;
using User.Infrastructure;
using ProductEntity = Product.Domain.Entities.Product;
using UserEntity = User.Domain.Entities.User;

if (args is ["--help"])
{
    Console.WriteLine("Seed migrations and demo/admin data: dotnet ModularMonolith.Seed.dll");
    Console.WriteLine("Grant membership to an existing confirmed account: dotnet ModularMonolith.Seed.dll grant-membership <email> <tenant-id>");
    return;
}
if (args.Length != 0 && (args.Length != 3 || args[0] != "grant-membership"))
{
    throw new ArgumentException("Expected no arguments, or: grant-membership <email> <tenant-id>.");
}

// Build the existing Auth composition root without starting an HTTP listener.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
var settingsFile = builder.Configuration["Auth:SettingsFile"];
if (!string.IsNullOrWhiteSpace(settingsFile))
{
    builder.Configuration.AddJsonFile(settingsFile, optional: false, reloadOnChange: false)
        .AddEnvironmentVariables();
}
builder.Services.AddAuthModule(builder.Configuration, builder.Environment);
await using var application = builder.Build();
await using var scope = application.Services.CreateAsyncScope();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
var cancellationToken = cancellation.Token;
var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
var authSeed = scope.ServiceProvider.GetRequiredService<AuthSeedService>();
var configuration = builder.Configuration;
var tenants = configuration.GetSection("Tenants").GetChildren().ToDictionary(
    tenant => tenant.Key,
    tenant => !string.IsNullOrWhiteSpace(tenant.Value) ? tenant.Value : throw new InvalidOperationException($"Tenant '{tenant.Key}' has no connection string."),
    StringComparer.Ordinal);
var allowedTenantIds = configuration.GetSection("Auth:Tenants").GetChildren()
    .Select(tenant => tenant["Id"] ?? throw new InvalidOperationException("Auth tenant Id is required."))
    .ToArray();
if (allowedTenantIds.Length == 0 || allowedTenantIds.Any(id => !tenants.ContainsKey(id)))
{
    throw new InvalidOperationException("Every Auth tenant must have an explicitly configured Tenants connection string.");
}

if (args.Length == 3)
{
    if (!allowedTenantIds.Contains(args[2], StringComparer.Ordinal))
    {
        throw new ArgumentException("The requested tenant is outside the configured Auth tenant allowlist.");
    }
    await authSeed.GrantMembershipAsync(args[1], args[2], cancellationToken);
    Console.WriteLine("Membership granted. Existing business User profiles were not modified.");
    return;
}

var connectionString = configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
await auth.Database.MigrateAsync(cancellationToken);
await EnsureUserDataAsync(connectionString, cancellationToken);
await EnsureProductDataAsync(connectionString, cancellationToken);
await using (var orders = new OrderDbContext(new DbContextOptionsBuilder<OrderDbContext>().UseSqlServer(connectionString).Options))
{
    await orders.Database.MigrateAsync(cancellationToken);
}
await using (var sources = new DataSourceDbContext(new DbContextOptionsBuilder<DataSourceDbContext>().UseSqlServer(connectionString).Options))
{
    await sources.Database.MigrateAsync(cancellationToken);
}
foreach (var tenant in tenants)
{
    await using var context = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseSqlServer(tenant.Value).Options);
    await context.Database.MigrateAsync(cancellationToken);
    Console.WriteLine($"Tenant '{tenant.Key}' migrations completed.");
}

var email = configuration["Seed:AdministratorEmail"] ?? throw new InvalidOperationException("Seed:AdministratorEmail is required.");
var password = configuration["Seed:AdministratorPassword"] ?? throw new InvalidOperationException("Seed:AdministratorPassword is required.");
await authSeed.EnsureAdministratorAsync(email, password,
    configuration["Seed:AdministratorName"] ?? "Platform administrator", allowedTenantIds, cancellationToken);
Console.WriteLine("Auth, shared, and all configured tenant migrations completed; administrator and demo seed are ready.");

static async Task EnsureUserDataAsync(string connectionString, CancellationToken cancellationToken)
{
    await using var context = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseSqlServer(connectionString).Options);
    await context.Database.MigrateAsync(cancellationToken);
    if (await context.Users.AnyAsync(cancellationToken))
    {
        return;
    }
    var users = Enumerable.Range(1, 10)
        .Select(i => new UserEntity(Guid.NewGuid(), $"User {i}", $"user{i}@example.com"));
    await context.Users.AddRangeAsync(users, cancellationToken);
    await context.SaveChangesAsync(cancellationToken);
}

static async Task EnsureProductDataAsync(string connectionString, CancellationToken cancellationToken)
{
    await using var context = new ProductDbContext(new DbContextOptionsBuilder<ProductDbContext>().UseSqlServer(connectionString).Options);
    await context.Database.MigrateAsync(cancellationToken);
    if (await context.Products.AnyAsync(cancellationToken))
    {
        return;
    }
    var products = Enumerable.Range(1, 30)
        .Select(i => new ProductEntity(Guid.NewGuid(), $"Product {i}", 10 + i));
    await context.Products.AddRangeAsync(products, cancellationToken);
    await context.SaveChangesAsync(cancellationToken);
}