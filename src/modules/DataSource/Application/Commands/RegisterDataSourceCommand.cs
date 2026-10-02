using DataSource.Application.Abstractions;
using DataSource.Domain.Enums;
using Mediator;

namespace DataSource.Application.Commands;

public record RegisterDataSourceCommand(
    Guid UserId,
    string Name,
    ProviderType Provider,
    string ConnectionString) : IRequest<DataSourceDto>;
