using Mediator;
using Microsoft.Extensions.Logging;
using User.Domain.Events;

namespace User.Application.Events;

public class UserRegisteredHandler : INotificationHandler<UserRegistered>
{
    private readonly ILogger<UserRegisteredHandler> _logger;

    public UserRegisteredHandler(ILogger<UserRegisteredHandler> logger) => _logger = logger;

    public ValueTask Handle(UserRegistered notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("User registered: {UserId} {Email}", notification.UserId, notification.Email);
        return ValueTask.CompletedTask;
    }
}