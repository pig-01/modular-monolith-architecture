using DataSource.Application.Abstractions;
using Mediator;

namespace DataSource.Application.Queries;

public record GetUserDataSourcesQuery(Guid UserId) : IRequest<IReadOnlyList<DataSourceDto>>;