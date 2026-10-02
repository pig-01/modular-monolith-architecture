using Microsoft.EntityFrameworkCore;

namespace User.Infrastructure.MultiTenant;

/// <summary>
/// Factory that creates a <typeparamref name="TContext"/> configured for the
/// current tenant's database connection string.
/// </summary>
public interface ITenantDbContextFactory<TContext> where TContext : DbContext
{
    TContext CreateDbContext();
}