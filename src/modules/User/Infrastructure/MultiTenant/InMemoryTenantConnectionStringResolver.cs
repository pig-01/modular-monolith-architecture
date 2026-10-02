using Microsoft.Extensions.Configuration;

namespace User.Infrastructure.MultiTenant;

/// <summary>
/// Resolves tenant connection strings from the "Tenants" section in appsettings.json.
/// For production, replace with a DB-backed implementation that stores encrypted connection strings.
/// </summary>
public class InMemoryTenantConnectionStringResolver : ITenantConnectionStringResolver
{
    private readonly IConfiguration _configuration;

    public InMemoryTenantConnectionStringResolver(IConfiguration configuration) => _configuration = configuration;

    public string Resolve(string tenantId)
    {
        var connStr = _configuration[$"Tenants:{tenantId}"];
        if (string.IsNullOrEmpty(connStr))
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' is not configured. Add it to the 'Tenants' section in appsettings.json.");
        }

        return connStr;
    }
}