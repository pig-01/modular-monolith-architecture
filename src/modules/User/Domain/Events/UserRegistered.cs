using Mediator;

namespace User.Domain.Events;

public record UserRegistered(Guid UserId, string Name, string Email) : INotification;
