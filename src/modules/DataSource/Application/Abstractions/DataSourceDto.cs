using DataSource.Domain.Enums;

namespace DataSource.Application.Abstractions;

public record DataSourceDto(Guid Id, Guid UserId, string Name, ProviderType Provider);