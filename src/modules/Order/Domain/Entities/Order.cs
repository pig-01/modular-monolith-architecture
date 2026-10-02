using Mediator;
using Order.Domain.Events;

namespace Order.Domain.Entities;

public class Order
{
    private readonly List<OrderItem> _items = new();
    private readonly List<INotification> _domainEvents = new();

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<INotification> DomainEvents => _domainEvents.AsReadOnly();

    private Order() { }

    public Order(Guid id, Guid userId, IEnumerable<OrderItem> items)
    {
        Id = id;
        UserId = userId;
        _items.AddRange(items);
        AddDomainEvent(new OrderPlaced(Id, UserId, _items.Select(i => new OrderedProduct(i.ProductId, i.Quantity)).ToList()));
    }

    public decimal Total => _items.Sum(i => i.Price * i.Quantity);

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void AddDomainEvent(INotification notification) => _domainEvents.Add(notification);
}
