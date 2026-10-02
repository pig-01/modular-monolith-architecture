using Mediator;

namespace Product.Domain.Events;

public record ProductCreated(Guid ProductId, string Name, decimal Price) : INotification;