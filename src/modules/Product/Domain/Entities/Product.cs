using Mediator;
using Product.Domain.Events;

namespace Product.Domain.Entities;

public class Product
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public decimal Price { get; private set; }
    private readonly List<INotification> _domainEvents = new();
    public IReadOnlyCollection<INotification> DomainEvents => _domainEvents.AsReadOnly();

    private Product() { }

    public Product(Guid id, string name, decimal price)
    {
        Id = id;
        Name = name;
        Price = price;
        AddDomainEvent(new ProductCreated(Id, Name, Price));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void AddDomainEvent(INotification notification) => _domainEvents.Add(notification);
}
