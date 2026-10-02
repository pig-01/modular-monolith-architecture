using Mediator;
using Order.Application.Abstractions;
using Order.Application.Mapping;
using Order.Domain.Entities;
using Order.Domain.Events;
using Order.Infrastructure;
using Order.IntegrationEvent.Events;

namespace Order.Application.Commands;

public class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, OrderDto>
{
    private readonly OrderDbContext _dbContext;
    private readonly IMediator _mediator;

    public PlaceOrderCommandHandler(OrderDbContext dbContext, IMediator mediator)
    {
        _dbContext = dbContext;
        _mediator = mediator;
    }

    public async ValueTask<OrderDto> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var items = request.Items.Select(i => new OrderItem(i.ProductId, i.Quantity, i.Price)).ToList();
        var order = new Order.Domain.Entities.Order(Guid.NewGuid(), request.UserId, items);

        await _dbContext.Orders.AddAsync(order, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await PublishDomainEvents(order, cancellationToken);
        await _mediator.Publish(new OrderPlacedIntegrationEvent(order.Id, order.UserId,
            order.Items.Select(i => new OrderPlacedProduct(i.ProductId, i.Quantity)).ToList()), cancellationToken);
        order.ClearDomainEvents();

        return OrderMapper.ToDto(order);
    }

    private async Task PublishDomainEvents(Order.Domain.Entities.Order order, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in order.DomainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }
}
