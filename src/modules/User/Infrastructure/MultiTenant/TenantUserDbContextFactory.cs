using Microsoft.EntityFrameworkCore;

namespace User.Infrastructure.MultiTenant;

/// <summary>
/// Creates a UserDbContext pointing at the current tenant's database.
/// Schema migrations are applied by the deployment seed before serving requests.
/// </summary>
public class TenantUserDbContextFactory
{
    private readonly ITenantProvider _tenantProvider;
    private readonly ITenantConnectionStringResolver _resolver;

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

        return new UserDbContext(options);
    }
}