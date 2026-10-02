using DataSource.Application.Abstractions;
using DataSource.Application.Mapping;
using DataSource.Infrastructure;
using DataSource.Infrastructure.MultiDb;
using Microsoft.EntityFrameworkCore;

namespace DataSource.Application.Services;

/// <summary>
/// Queries all registered data sources in parallel using Task.WhenAll.
/// Each result is tagged with SourceName and ProviderType so the caller
/// can identify which database each record originated from.
/// </summary>
public class MultiSourceQueryService : IMultiSourceQueryService
{
    private readonly IMultiDbContextFactory _factory;
    private readonly DataSourceDbContext _dbContext;

    public MultiSourceQueryService(IMultiDbContextFactory factory, DataSourceDbContext dbContext)
    {
        _factory = factory;
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<SourcedResult<UserData>>> QueryUsersAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var sources = await _dbContext.DataSources
            .Where(ds => ds.UserId == userId)
            .AsNoTracking()
            .ToListAsync(ct);

        if (sources.Count == 0)
            return Array.Empty<SourcedResult<UserData>>();

        var tasks = sources.Select(source => QuerySingleSourceAsync(source, ct));
        var results = await Task.WhenAll(tasks);

        return results
            .SelectMany(r => r)
            .ToList()
            .AsReadOnly();
    }

    private async Task<IEnumerable<SourcedResult<UserData>>> QuerySingleSourceAsync(
        Domain.Entities.DataSource source,
        CancellationToken ct)
    {
        await using var ctx = _factory.CreateUserContext(source);

        var users = await ctx.Set<ExternalUser>()
            .AsNoTracking()
            .ToListAsync(ct);

        return users.Select(u =>
            new SourcedResult<UserData>(
                DataSourceMapper.ToUserData(u),
                source.Name,
                source.Provider));
    }
}
