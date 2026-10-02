namespace Order.Application.Abstractions;

public record OrderItemDto(Guid ProductId, int Quantity, decimal Price);