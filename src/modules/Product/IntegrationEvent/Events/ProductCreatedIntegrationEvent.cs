using Mediator;

namespace Product.IntegrationEvent.Events;

public record ProductCreatedIntegrationEvent(Guid ProductId, string Name, decimal Price) : INotification;