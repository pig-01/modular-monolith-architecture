using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using User.Application.MultiTenant;
using User.Infrastructure.MultiTenant;

namespace User.IntegrationTest;

public class TenantUserDbContextFactoryTests
{
    [Fact]
    public void Selecting_a_tenant_does_not_connect_or_migrate_during_a_request()
    {
        const string connection = "Server=127.0.0.1,1;Database=TenantOne;User Id=test;Password=not-a-real-password;Connect Timeout=1;TrustServerCertificate=True";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Tenants:tenant1"] = connection }).Build();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "tenant1")], "test"))
        };
        var factory = new TenantUserDbContextFactory(
            new JwtTenantProvider(new HttpContextAccessor { HttpContext = context }),
            new InMemoryTenantConnectionStringResolver(configuration));

        using var database = factory.CreateDbContext();

        Assert.Equal("TenantOne", database.Database.GetDbConnection().Database);
        Assert.Equal("127.0.0.1,1", database.Database.GetDbConnection().DataSource);
    }

    [Fact]
    public void Unauthenticated_claim_cannot_select_a_tenant()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "tenant1")]))
        };
        var provider = new JwtTenantProvider(new HttpContextAccessor { HttpContext = context });

        Assert.Null(provider.GetTenantId());
    }

    [Fact]
    public void A_header_cannot_select_a_tenant()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = "tenant2";
        var provider = new JwtTenantProvider(new HttpContextAccessor { HttpContext = context });

        Assert.Null(provider.GetTenantId());
    }

    [Fact]
    public void Authenticated_claim_is_used_even_when_a_different_header_is_present()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "tenant1")], "test"))
        };
        context.Request.Headers["X-Tenant-Id"] = "tenant2";
        var provider = new JwtTenantProvider(new HttpContextAccessor { HttpContext = context });

        Assert.Equal("tenant1", provider.GetTenantId());
    }

    [Fact]
    public void Unknown_tenant_is_rejected_instead_of_using_a_default_database()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Server=unused" }).Build();
        var resolver = new InMemoryTenantConnectionStringResolver(configuration);

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("unknown"));
    }

    [Fact]
    public void Missing_http_context_does_not_select_a_default_tenant()
    {
        var provider = new JwtTenantProvider(new HttpContextAccessor());

        Assert.Null(provider.GetTenantId());
    }
}