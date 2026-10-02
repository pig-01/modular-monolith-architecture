using Mediator;
using Microsoft.EntityFrameworkCore;
using Order.Application.Mapping;
using Order.Infrastructure;

namespace Order.Application.Queries;

public record OrderItemDto(Guid ProductId, int Quantity, decimal Price);
public record OrderDto(Guid Id, Guid UserId, decimal Total, IReadOnlyList<OrderItemDto> Items);
public record GetOrdersQuery : IRequest<IReadOnlyList<OrderDto>>;
public record GetOrderByIdQuery(Guid Id) : IRequest<OrderDto?>;

public sealed class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, IReadOnlyList<OrderDto>>
{
    private readonly OrderDbContext _dbContext;

    public GetOrdersQueryHandler(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<IReadOnlyList<OrderDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .ProjectToDto()
            .ToListAsync(cancellationToken);
    }
}

public sealed class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    private readonly OrderDbContext _dbContext;

    public GetOrderByIdQueryHandler(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<OrderDto?> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.Id == request.Id)
            .ProjectToDto()
            .FirstOrDefaultAsync(cancellationToken);
    }
}
