namespace Order.Application.Abstractions;

public record OrderDto(Guid Id, Guid UserId, IReadOnlyCollection<OrderItemDto> Items, decimal Total);