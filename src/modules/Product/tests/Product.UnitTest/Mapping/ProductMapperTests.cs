using Microsoft.EntityFrameworkCore;
using Product.Application.Mapping;
using Product.Infrastructure;
using ProductEntity = Product.Domain.Entities.Product;
using QueryProductDto = Product.Application.Queries.ProductDto;

namespace Product.UnitTest.Mapping;

[Trait("Category", "Unit")]
public class ProductMapperTests
{
    [Theory]
    [InlineData("Free", 0)]
    [InlineData("零件", 1)]
    [InlineData("", 1234567)]
    public void ToDto_PreservesFieldsDecimalPrecisionAndDomainEvents(string name, int cents)
    {
        var price = cents / 100m;
        ProductEntity source = new(Guid.NewGuid(), name, price);
        var domainEvent = Assert.Single(source.DomainEvents);

        var result = ProductMapper.ToDto(source);

        Assert.Equal(new Application.Abstractions.ProductDto(source.Id, name, price), result);
        Assert.Same(domainEvent, Assert.Single(source.DomainEvents));
    }

    [Fact]
    public void ProjectToDto_PreservesFilteringOrderingAndEmptyResults()
    {
        ProductEntity first = new(Guid.NewGuid(), "First", 9.99m);
        ProductEntity second = new(Guid.NewGuid(), "Second", 12.50m);
        ProductEntity excluded = new(Guid.NewGuid(), "Free", 0m);
        var source = new[] { second, excluded, first }.AsQueryable();

        List<QueryProductDto> result = source.Where(product => product.Price > 0)
            .OrderBy(product => product.Price).ProjectToDto().ToList();

        Assert.Equal(new[]
        {
            new QueryProductDto(first.Id, "First", 9.99m),
            new QueryProductDto(second.Id, "Second", 12.50m)
        }, result);
        Assert.Null(source.Where(product => product.Id == Guid.Empty).ProjectToDto().FirstOrDefault());
        Assert.Empty(Array.Empty<ProductEntity>().AsQueryable().ProjectToDto());
    }

    [Fact]
    public void ProjectToDto_GeneratesSameSqlAsOriginalProjection()
    {
        using ProductDbContext db = new(new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer("Server=localhost;Database=MappingTests;Integrated Security=true;TrustServerCertificate=true")
            .Options);
        Guid id = Guid.NewGuid();
        var query = db.Products.AsNoTracking().Where(product => product.Id == id);
        var originalSql = query.Select(product => new QueryProductDto(product.Id, product.Name, product.Price))
            .ToQueryString();

        Assert.Equal(originalSql, query.ProjectToDto().ToQueryString());
    }
}