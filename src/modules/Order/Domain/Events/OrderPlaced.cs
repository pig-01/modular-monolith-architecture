using Mediator;

namespace Order.Domain.Events;

public record OrderedProduct(Guid ProductId, int Quantity);

public record OrderPlaced(Guid OrderId, Guid UserId, IReadOnlyCollection<OrderedProduct> Products) : INotification;