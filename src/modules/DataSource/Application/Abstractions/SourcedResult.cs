using DataSource.Domain.Enums;

namespace DataSource.Application.Abstractions;

/// <summary>
/// Wraps a query result item with metadata identifying which data source it came from.
/// </summary>
public record SourcedResult<T>(T Data, string SourceName, ProviderType Provider);