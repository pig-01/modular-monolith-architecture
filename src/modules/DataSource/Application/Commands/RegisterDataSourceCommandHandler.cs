using DataSource.Application.Abstractions;
using DataSource.Infrastructure;
using Mediator;

namespace DataSource.Application.Commands;

public class RegisterDataSourceCommandHandler : IRequestHandler<RegisterDataSourceCommand, DataSourceDto>
{
    private readonly DataSourceDbContext _dbContext;

    public RegisterDataSourceCommandHandler(DataSourceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<DataSourceDto> Handle(RegisterDataSourceCommand request, CancellationToken cancellationToken)
    {
        var entity = new Domain.Entities.DataSource(
            Guid.NewGuid(),
            request.UserId,
            request.Name,
            request.Provider,
            request.ConnectionString);

        _dbContext.DataSources.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DataSourceDto(entity.Id, entity.UserId, entity.Name, entity.Provider);
    }
}
