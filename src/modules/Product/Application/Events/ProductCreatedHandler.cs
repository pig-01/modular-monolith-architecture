using Mediator;
using Microsoft.Extensions.Logging;
using Product.Domain.Events;

namespace Product.Application.Events;

public class ProductCreatedHandler : INotificationHandler<ProductCreated>
{
    private readonly ILogger<ProductCreatedHandler> _logger;

    public ProductCreatedHandler(ILogger<ProductCreatedHandler> logger) => _logger = logger;

    public ValueTask Handle(ProductCreated notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Product created: {ProductId} {Name}", notification.ProductId, notification.Name);
        return ValueTask.CompletedTask;
    }
}