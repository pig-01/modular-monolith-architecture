using DataSource.Application.Abstractions;
using DataSource.Application.Mapping;
using DataSource.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DataSource.Application.Queries;

public class GetUserDataSourcesQueryHandler : IRequestHandler<GetUserDataSourcesQuery, IReadOnlyList<DataSourceDto>>
{
    private readonly DataSourceDbContext _dbContext;

    public GetUserDataSourcesQueryHandler(DataSourceDbContext dbContext) => _dbContext = dbContext;

    public async ValueTask<IReadOnlyList<DataSourceDto>> Handle(
        GetUserDataSourcesQuery request,
        CancellationToken cancellationToken) => await _dbContext.DataSources
            .Where(ds => ds.UserId == request.UserId)
            .AsNoTracking()
            .ProjectToDto()
            .ToListAsync(cancellationToken);
}