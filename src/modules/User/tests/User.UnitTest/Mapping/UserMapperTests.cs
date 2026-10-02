using Microsoft.EntityFrameworkCore;
using User.Application.Mapping;
using User.Infrastructure;
using QueryUserDto = User.Application.Queries.UserDto;
using UserEntity = User.Domain.Entities.User;

namespace User.UnitTest.Mapping;

[Trait("Category", "Unit")]
public class UserMapperTests
{
    [Theory]
    [InlineData("Alice", "alice@example.com")]
    [InlineData("陳小明", "ming@example.com")]
    [InlineData("", "")]
    public void ToDto_PreservesFieldsAndDomainEvents(string name, string email)
    {
        UserEntity source = new(Guid.NewGuid(), name, email);
        var domainEvent = Assert.Single(source.DomainEvents);

        var result = UserMapper.ToDto(source);

        Assert.Equal(new Application.Abstractions.UserDto(source.Id, name, email), result);
        Assert.Same(domainEvent, Assert.Single(source.DomainEvents));
    }

    [Fact]
    public void ProjectToDto_PreservesFilteringOrderingAndEmptyResults()
    {
        UserEntity alice = new(Guid.NewGuid(), "Alice", "alice@example.com");
        UserEntity bob = new(Guid.NewGuid(), "Bob", "bob@example.com");
        UserEntity excluded = new(Guid.NewGuid(), "Other", "other@elsewhere.test");
        var source = new[] { bob, excluded, alice }.AsQueryable();

        List<QueryUserDto> result = source.Where(user => user.Email.EndsWith("@example.com"))
            .OrderBy(user => user.Name).ProjectToDto().ToList();

        Assert.Equal(new[]
        {
            new QueryUserDto(alice.Id, "Alice", "alice@example.com"),
            new QueryUserDto(bob.Id, "Bob", "bob@example.com")
        }, result);
        Assert.Null(source.Where(user => user.Id == Guid.Empty).ProjectToDto().FirstOrDefault());
        Assert.Empty(Array.Empty<UserEntity>().AsQueryable().ProjectToDto());
    }

    [Fact]
    public void ProjectToDto_GeneratesSameSqlAsOriginalProjection()
    {
        using UserDbContext db = new(new DbContextOptionsBuilder<UserDbContext>()
            .UseSqlServer("Server=localhost;Database=MappingTests;Integrated Security=true;TrustServerCertificate=true")
            .Options);
        Guid id = Guid.NewGuid();
        var query = db.Users.AsNoTracking().Where(user => user.Id == id);
        var originalSql = query.Select(user => new QueryUserDto(user.Id, user.Name, user.Email))
            .ToQueryString();

        Assert.Equal(originalSql, query.ProjectToDto().ToQueryString());
    }
}