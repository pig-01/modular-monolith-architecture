using DataSource.Domain.Enums;
using DataSource.Infrastructure.MultiDb;
using Microsoft.EntityFrameworkCore;

namespace DataSource.UnitTest;

public class MultiDbContextFactoryTests
{
    [Theory]
    [InlineData(ProviderType.MSSQL, "Server=localhost;Database=ProviderTests;Integrated Security=true;TrustServerCertificate=true", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData(ProviderType.MySQL, "Server=localhost;Database=ProviderTests;User ID=test;Password=test", "MySql.EntityFrameworkCore")]
    [InlineData(ProviderType.PostgreSQL, "Host=localhost;Database=ProviderTests;Username=test;Password=test", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData(ProviderType.Oracle, "Data Source=localhost/FREEPDB1;User Id=test;Password=test", "Oracle.EntityFrameworkCore")]
    public void CreateUserContext_GeneratesReadOnlyUserQueryWithoutConnecting(
        ProviderType provider, string connectionString, string expectedProvider)
    {
        Domain.Entities.DataSource source = new(
            Guid.NewGuid(), Guid.NewGuid(), "Test source", provider, connectionString);
        MultiDbContextFactory factory = new();

        using var context = factory.CreateUserContext(source);

        Assert.Equal(expectedProvider, context.Database.ProviderName);
        Assert.Equal(QueryTrackingBehavior.NoTracking, context.ChangeTracker.QueryTrackingBehavior);
        var sql = context.Set<ExternalUser>()
            .Where(user => user.Name == "Alice")
            .Select(user => new { user.Id, user.Name, user.Email })
            .ToQueryString();
        Assert.Contains("SELECT", sql);
        Assert.Contains("Users", sql);
        Assert.Contains("WHERE", sql);
    }
}