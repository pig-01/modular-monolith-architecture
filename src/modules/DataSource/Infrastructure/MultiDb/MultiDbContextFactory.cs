using DataSource.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DataSource.Infrastructure.MultiDb;

public interface IMultiDbContextFactory
{
    /// <summary>
    /// Creates a DbContext for the given data source.
    /// The returned context is configured with the appropriate EF Core provider.
    /// Caller is responsible for disposing the context (use 'await using').
    /// </summary>
    DbContext CreateUserContext(Domain.Entities.DataSource source);
}

/// <summary>
/// Factory that creates EF Core DbContexts for heterogeneous external data sources.
/// Supports MSSQL, MySQL, PostgreSQL, and Oracle.
/// All created contexts share the same ExternalUserDbContext entity model,
/// ensuring the same schema contract is enforced regardless of the underlying provider.
/// </summary>
public class MultiDbContextFactory : IMultiDbContextFactory
{
    public DbContext CreateUserContext(Domain.Entities.DataSource source)
    {
        var builder = new DbContextOptionsBuilder<ExternalUserDbContext>()
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

        switch (source.Provider)
        {
            case ProviderType.MSSQL:
                builder.UseSqlServer(source.ConnectionString);
                break;

            case ProviderType.MySQL:
                builder.UseMySQL(source.ConnectionString);
                break;

            case ProviderType.PostgreSQL:
                builder.UseNpgsql(source.ConnectionString);
                break;

            case ProviderType.Oracle:
                builder.UseOracle(source.ConnectionString);
                break;

            default:
                throw new NotSupportedException(
                    $"Provider type '{source.Provider}' is not supported by MultiDbContextFactory.");
        }

        return new ExternalUserDbContext(builder.Options);
    }
}