using Mediator;

namespace Order.IntegrationEvent.Events;

public record OrderPlacedProduct(Guid ProductId, int Quantity);

public record OrderPlacedIntegrationEvent(Guid OrderId, Guid UserId, IReadOnlyCollection<OrderPlacedProduct> Products) : INotification;
