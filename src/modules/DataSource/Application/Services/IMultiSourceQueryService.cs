using DataSource.Application.Abstractions;

namespace DataSource.Application.Services;

public interface IMultiSourceQueryService
{
    /// <summary>
    /// Queries the "Users" table across all data sources registered by the given user.
    /// Results are returned in parallel and tagged with the originating source name and provider.
    /// </summary>
    Task<IReadOnlyList<SourcedResult<UserData>>> QueryUsersAsync(Guid userId, CancellationToken ct = default);
}