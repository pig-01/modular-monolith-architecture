using Mediator;
using User.Domain.Events;

namespace User.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string Email { get; private set; }
    private readonly List<INotification> _domainEvents = new();

    public IReadOnlyCollection<INotification> DomainEvents => _domainEvents.AsReadOnly();

    private User() { }

    public User(Guid id, string name, string email)
    {
        Id = id;
        Name = name;
        Email = email;
        AddDomainEvent(new UserRegistered(Id, Name, Email));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void AddDomainEvent(INotification notification) => _domainEvents.Add(notification);
}
