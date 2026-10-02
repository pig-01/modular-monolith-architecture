namespace User.Infrastructure.MultiTenant;

/// <summary>
/// Configuration for per-tenant database connection strings.
/// Bound from the "Tenants" section in appsettings.json.
/// </summary>
public class TenantOptions
{
    public const string SectionName = "Tenants";

    /// <summary>
    /// Maps tenant identifier to its database connection string.
    /// The getter-only property ensures the OrdinalIgnoreCase comparer is always
    /// used; the configuration binder populates the existing dictionary instance.
    /// </summary>
    public Dictionary<string, string> ConnectionStrings { get; }
        = new(StringComparer.OrdinalIgnoreCase);
}