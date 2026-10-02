using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using User.Application.MultiTenant;
using User.Infrastructure;
using User.Infrastructure.MultiTenant;

namespace User.IntegrationTest;

/// <summary>
/// Tests for the tenant-aware DbContext factory pattern.
/// Verifies that the correct connection string is selected based on the current
/// tenant identifier, and that HttpContextTenantProvider resolves the tenant
/// from JWT claims and request headers.
/// </summary>
public class TenantUserDbContextFactoryTests
{
    private const string DefaultConnectionString =
        "Server=default-host;Database=DefaultDb;Trusted_Connection=True;TrustServerCertificate=True;";
    private const string PremiumConnectionString =
        "Server=premium-host;Database=PremiumDb;Trusted_Connection=True;TrustServerCertificate=True;";

    // ---- helpers ----

    private static IOptions<TenantOptions> BuildTenantOptions()
    {
        TenantOptions options = new();
        options.ConnectionStrings[TenantConstants.Default] = DefaultConnectionString;
        options.ConnectionStrings[TenantConstants.Premium] = PremiumConnectionString;
        return Options.Create(options);
    }

    private static IConfiguration BuildConfiguration(string defaultCs = DefaultConnectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = defaultCs
            })
            .Build();

    private static TenantUserDbContextFactory CreateFactory(
        string tenantId,
        IOptions<TenantOptions>? tenantOptions = null,
        IConfiguration? configuration = null) =>
        new(
            new FakeTenantProvider(tenantId),
            tenantOptions ?? BuildTenantOptions(),
            configuration ?? BuildConfiguration());

    // ---- TenantUserDbContextFactory tests ----

    [Fact]
    public void CreateDbContext_ForDefaultTenant_UsesDefaultTenantConnectionString()
    {
        var factory = CreateFactory(TenantConstants.Default);

        using var context = factory.CreateDbContext();

        Assert.Equal(DefaultConnectionString, context.Database.GetConnectionString());
    }

    [Fact]
    public void CreateDbContext_ForPremiumTenant_UsesPremiumConnectionString()
    {
        var factory = CreateFactory(TenantConstants.Premium);

        using var context = factory.CreateDbContext();

        Assert.Equal(PremiumConnectionString, context.Database.GetConnectionString());
    }

    [Fact]
    public void CreateDbContext_ForUnknownTenant_FallsBackToDefaultConnection()
    {
        var factory = CreateFactory("unknown-tenant");

        using var context = factory.CreateDbContext();

        Assert.Equal(DefaultConnectionString, context.Database.GetConnectionString());
    }

    [Fact]
    public void CreateDbContext_DifferentTenants_ProduceDifferentConnectionStrings()
    {
        var defaultFactory = CreateFactory(TenantConstants.Default);
        var premiumFactory = CreateFactory(TenantConstants.Premium);

        using var defaultCtx = defaultFactory.CreateDbContext();
        using var premiumCtx = premiumFactory.CreateDbContext();

        Assert.NotEqual(
            defaultCtx.Database.GetConnectionString(),
            premiumCtx.Database.GetConnectionString());
    }

    [Fact]
    public void CreateDbContext_MultipleCallsSameTenant_ReturnIndependentContexts()
    {
        var factory = CreateFactory(TenantConstants.Premium);

        using var ctx1 = factory.CreateDbContext();
        using var ctx2 = factory.CreateDbContext();

        Assert.NotSame(ctx1, ctx2);
        Assert.Equal(ctx1.Database.GetConnectionString(), ctx2.Database.GetConnectionString());
    }

    [Fact]
    public void TenantUserDbContextFactory_MissingDefaultConnection_Throws()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        var tenantOptions = Options.Create(new TenantOptions()); // no per-tenant entries

        Assert.Throws<InvalidOperationException>(() =>
            new TenantUserDbContextFactory(
                new FakeTenantProvider(TenantConstants.Default),
                tenantOptions,
                emptyConfig));
    }

    // ---- HttpContextTenantProvider tests ----

    [Fact]
    public void GetTenantId_FromJwtClaim_ReturnsTenantId()
    {
        DefaultHttpContext httpContext = new();
        httpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim("tenant_id", TenantConstants.Premium) }, "test"));

        HttpContextTenantProvider provider = new(new FakeHttpContextAccessor(httpContext));

        Assert.Equal(TenantConstants.Premium, provider.GetTenantId());
    }

    [Fact]
    public void GetTenantId_FromRequestHeader_ReturnsTenantId()
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Headers["X-Tenant-Id"] = TenantConstants.Premium;

        HttpContextTenantProvider provider = new(new FakeHttpContextAccessor(httpContext));

        Assert.Equal(TenantConstants.Premium, provider.GetTenantId());
    }

    [Fact]
    public void GetTenantId_JwtClaimTakesPrecedenceOverHeader()
    {
        DefaultHttpContext httpContext = new();
        httpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim("tenant_id", TenantConstants.Premium) }, "test"));
        httpContext.Request.Headers["X-Tenant-Id"] = TenantConstants.Default;

        HttpContextTenantProvider provider = new(new FakeHttpContextAccessor(httpContext));

        Assert.Equal(TenantConstants.Premium, provider.GetTenantId());
    }

    [Fact]
    public void GetTenantId_NoClaimOrHeader_ReturnsDefault()
    {
        DefaultHttpContext httpContext = new();

        HttpContextTenantProvider provider = new(new FakeHttpContextAccessor(httpContext));

        Assert.Equal(TenantConstants.Default, provider.GetTenantId());
    }

    [Fact]
    public void GetTenantId_NullHttpContext_ReturnsDefault()
    {
        HttpContextTenantProvider provider = new(new FakeHttpContextAccessor(null));

        Assert.Equal(TenantConstants.Default, provider.GetTenantId());
    }

    // ---- private fakes ----

    private sealed class FakeTenantProvider : ITenantProvider
    {
        private readonly string _tenantId;
        public FakeTenantProvider(string tenantId) => _tenantId = tenantId;
        public string GetTenantId() => _tenantId;
    }

    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
        public FakeHttpContextAccessor(HttpContext? context) => HttpContext = context;
    }
}