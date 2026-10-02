namespace User.Infrastructure.MultiTenant;

public interface ITenantConnectionStringResolver
{
    string Resolve(string tenantId);
}