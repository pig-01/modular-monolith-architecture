using Mediator;
using Microsoft.Extensions.Logging;
using Order.Domain.Events;

namespace Order.Application.Events;

public class OrderPlacedHandler : INotificationHandler<OrderPlaced>
{
    private readonly ILogger<OrderPlacedHandler> _logger;

    public OrderPlacedHandler(ILogger<OrderPlacedHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Order placed: {OrderId} by {UserId}", notification.OrderId, notification.UserId);
        return ValueTask.CompletedTask;
    }
}
