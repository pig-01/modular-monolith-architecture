using Mediator;
using Order.Application.Abstractions;

namespace Order.Application.Commands;

public record PlaceOrderItem(Guid ProductId, int Quantity, decimal Price);

public record PlaceOrderCommand(Guid UserId, IReadOnlyCollection<PlaceOrderItem> Items) : IRequest<OrderDto>;