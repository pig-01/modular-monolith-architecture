using Microsoft.EntityFrameworkCore;
using Order.Application.Mapping;
using Order.Domain.Entities;
using Order.Infrastructure;
using OrderEntity = Order.Domain.Entities.Order;
using QueryOrderDto = Order.Application.Queries.OrderDto;
using QueryOrderItemDto = Order.Application.Queries.OrderItemDto;

namespace Order.UnitTest.Mapping;

[Trait("Category", "Unit")]
public class OrderMapperTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1998)]
    [InlineData(2, 2748)]
    public void Mapping_PreservesBothDtoContractsItemsAndTotal(int itemCount, int expectedCents)
    {
        List<OrderItem> items = new[]
        {
            new OrderItem(Guid.NewGuid(), 2, 9.99m),
            new OrderItem(Guid.NewGuid(), 3, 2.50m)
        }.Take(itemCount).ToList();
        OrderEntity source = new(Guid.NewGuid(), Guid.NewGuid(), items);
        var domainEvent = Assert.Single(source.DomainEvents);

        var commandResult = OrderMapper.ToDto(source);
        var queryResult = new[] { source }.AsQueryable().ProjectToDto().Single();

        Assert.Equal(source.Id, commandResult.Id);
        Assert.Equal(source.UserId, commandResult.UserId);
        Assert.Equal(expectedCents / 100m, commandResult.Total);
        Assert.Equal(items.Select(item => (item.ProductId, item.Quantity, item.Price)),
            commandResult.Items.Select(item => (item.ProductId, item.Quantity, item.Price)));
        Assert.Equal(source.Id, queryResult.Id);
        Assert.Equal(source.UserId, queryResult.UserId);
        Assert.Equal(expectedCents / 100m, queryResult.Total);
        Assert.Equal(items.Select(item => (item.ProductId, item.Quantity, item.Price)),
            queryResult.Items.Select(item => (item.ProductId, item.Quantity, item.Price)));
        Assert.Same(domainEvent, Assert.Single(source.DomainEvents));
    }

    [Fact]
    public void ProjectToDto_PreservesIdFilterAndEmptyResults()
    {
        OrderEntity first = new(Guid.NewGuid(), Guid.NewGuid(), []);
        OrderEntity second = new(Guid.NewGuid(), Guid.NewGuid(), []);
        var source = new[] { first, second }.AsQueryable();

        var result = source.Where(order => order.Id == second.Id).ProjectToDto().Single();

        Assert.Equal(second.Id, result.Id);
        Assert.Null(source.Where(order => order.Id == Guid.Empty).ProjectToDto().FirstOrDefault());
        Assert.Empty(Array.Empty<OrderEntity>().AsQueryable().ProjectToDto());
    }

    [Fact]
    public void ProjectToDto_GeneratesSameSqlAsOriginalProjection()
    {
        using OrderDbContext db = new(new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer("Server=localhost;Database=MappingTests;Integrated Security=true;TrustServerCertificate=true")
            .Options);
        Guid id = Guid.NewGuid();
        var query = db.Orders.AsNoTracking().Include(order => order.Items).Where(order => order.Id == id);
        var originalSql = query.Select(order => new QueryOrderDto(order.Id, order.UserId, order.Total,
                order.Items.Select(item => new QueryOrderItemDto(item.ProductId, item.Quantity, item.Price)).ToList()))
            .ToQueryString();

        Assert.Equal(originalSql, query.ProjectToDto().ToQueryString());
    }
}