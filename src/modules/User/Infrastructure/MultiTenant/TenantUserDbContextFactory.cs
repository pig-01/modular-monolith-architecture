using Microsoft.EntityFrameworkCore;

namespace User.Infrastructure.MultiTenant;

/// <summary>
/// Creates a UserDbContext pointing at the current tenant's database.
/// On first access per tenant, runs EF Core migrations to guarantee schema consistency
/// across all tenant databases.
/// </summary>
public class TenantUserDbContextFactory
{
    private readonly ITenantProvider _tenantProvider;
    private readonly ITenantConnectionStringResolver _resolver;

    // Tracks which tenants have already been migrated in this process lifetime
    // to avoid calling MigrateAsync on every request.
    private static readonly HashSet<string> _migratedTenants = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _migrationLock = new();

    public TenantUserDbContextFactory(ITenantProvider tenantProvider, ITenantConnectionStringResolver resolver)
    {
        _tenantProvider = tenantProvider;
        _resolver = resolver;
    }

    public UserDbContext CreateDbContext()
    {
        var tenantId = _tenantProvider.GetTenantId()
            ?? throw new InvalidOperationException(
                "Tenant ID not found. Ensure the JWT token contains a 'tenant_id' claim.");

        var connStr = _resolver.Resolve(tenantId);

        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseSqlServer(connStr)
            .Options;

        UserDbContext ctx = new(options);

        EnsureMigratedAsync(ctx, tenantId).GetAwaiter().GetResult();

        return ctx;
    }

    private static async Task EnsureMigratedAsync(UserDbContext ctx, string tenantId)
    {
        bool needsMigration;
        lock (_migrationLock)
        {
            needsMigration = _migratedTenants.Add(tenantId);
        }

        if (needsMigration)
            await ctx.Database.MigrateAsync();
    }
}