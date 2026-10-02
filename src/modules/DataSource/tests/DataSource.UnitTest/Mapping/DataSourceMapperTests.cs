using DataSource.Application.Abstractions;
using DataSource.Application.Mapping;
using DataSource.Domain.Enums;
using DataSource.Infrastructure;
using DataSource.Infrastructure.MultiDb;
using Microsoft.EntityFrameworkCore;
using DataSourceEntity = DataSource.Domain.Entities.DataSource;

namespace DataSource.UnitTest.Mapping;

[Trait("Category", "Unit")]
public class DataSourceMapperTests
{
    [Theory]
    [InlineData(ProviderType.MSSQL)]
    [InlineData(ProviderType.MySQL)]
    [InlineData(ProviderType.PostgreSQL)]
    [InlineData(ProviderType.Oracle)]
    public void ToDto_PreservesFieldsForEveryProvider(ProviderType provider)
    {
        DataSourceEntity source = new(Guid.NewGuid(), Guid.NewGuid(), "資料來源", provider, "private-connection");
        var createdAt = source.CreatedAt;

        var result = DataSourceMapper.ToDto(source);

        Assert.Equal(new DataSourceDto(source.Id, source.UserId, "資料來源", provider), result);
        Assert.Equal("private-connection", source.ConnectionString);
        Assert.Equal(createdAt, source.CreatedAt);
    }

    [Fact]
    public void ProjectToDto_PreservesUserFilterAndEmptyResults()
    {
        Guid userId = Guid.NewGuid();
        DataSourceEntity owned = new(Guid.NewGuid(), userId, "Owned", ProviderType.PostgreSQL, "private-owned");
        DataSourceEntity other = new(Guid.NewGuid(), Guid.NewGuid(), "Other", ProviderType.MySQL, "private-other");
        var source = new[] { other, owned }.AsQueryable();

        var result = source.Where(item => item.UserId == userId).ProjectToDto().Single();

        Assert.Equal(new DataSourceDto(owned.Id, userId, "Owned", ProviderType.PostgreSQL), result);
        Assert.Empty(source.Where(item => item.UserId == Guid.Empty).ProjectToDto());
        Assert.Empty(Array.Empty<DataSourceEntity>().AsQueryable().ProjectToDto());
    }

    [Fact]
    public void ProjectToDto_GeneratesSameSqlWithoutPrivateFields()
    {
        using DataSourceDbContext db = new(new DbContextOptionsBuilder<DataSourceDbContext>()
            .UseSqlServer("Server=localhost;Database=MappingTests;Integrated Security=true;TrustServerCertificate=true")
            .Options);
        Guid userId = Guid.NewGuid();
        var query = db.DataSources.Where(source => source.UserId == userId).AsNoTracking();
        var originalSql = query.Select(source => new DataSourceDto(source.Id, source.UserId, source.Name, source.Provider))
            .ToQueryString();

        var sql = query.ProjectToDto().ToQueryString();

        Assert.Equal(originalSql, sql);
        Assert.DoesNotContain("ConnectionString", sql);
        Assert.DoesNotContain("CreatedAt", sql);
    }

    [Theory]
    [InlineData("外部使用者", "external@example.com")]
    [InlineData("", "")]
    public void ToUserData_PreservesExternalUserFields(string name, string email)
    {
        ExternalUser source = new() { Id = Guid.NewGuid(), Name = name, Email = email };

        UserData result = DataSourceMapper.ToUserData(source);

        Assert.Equal(new UserData(source.Id, name, email), result);
    }
}