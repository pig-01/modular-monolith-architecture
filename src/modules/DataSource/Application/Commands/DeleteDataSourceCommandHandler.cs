using DataSource.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DataSource.Application.Commands;

public class DeleteDataSourceCommandHandler : IRequestHandler<DeleteDataSourceCommand, bool>
{
    private readonly DataSourceDbContext _dbContext;

    public DeleteDataSourceCommandHandler(DataSourceDbContext dbContext) => _dbContext = dbContext;

    public async ValueTask<bool> Handle(DeleteDataSourceCommand request, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.DataSources
            .FirstOrDefaultAsync(
                ds => ds.Id == request.DataSourceId && ds.UserId == request.UserId,
                cancellationToken);

        if (entity is null) return false;

        _dbContext.DataSources.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}