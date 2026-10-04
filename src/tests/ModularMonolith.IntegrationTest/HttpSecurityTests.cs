using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ModularMonolith.IntegrationTest;

public class HttpSecurityTests
{
    private const string TestKey = "test-only-signing-key-never-used-outside-the-test-host-0123456789abcdef";

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task Malformed_json_returns_a_safe_problem_details_response(string environmentName)
    {
        await using var app = new SecurityApplication(environmentName);
        using var client = app.CreateSecureClient();
        using var body = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/auth/login", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        Assert.False(problem.TryGetProperty("exception", out _));
    }

    [Fact]
    public async Task Login_requires_csrf_even_before_authentication()
    {
        await using var app = new SecurityApplication();
        using var client = app.CreateSecureClient();

        using var response = await client.PostAsJsonAsync("/auth/login", new { email = "member@example.test", password = "a long test password" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("csrf_failed", problem.GetProperty("code").GetString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Csrf_bootstrap_sets_a_secure_host_only_cookie()
    {
        await using var app = new SecurityApplication();
        using var client = app.CreateSecureClient();

        using var response = await client.GetAsync("/auth/csrf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(token.GetProperty("requestToken").GetString()));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-", cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Theory]
    [InlineData("https://allowed.example.test", true)]
    [InlineData("https://attacker.example.test", false)]
    public async Task Credentialed_cors_only_allows_configured_origins(string origin, bool allowed)
    {
        await using var app = new SecurityApplication();
        using var client = app.CreateSecureClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/auth/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-csrf-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
        {
            Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        }
    }

    [Fact]
    public async Task Mixed_bearer_and_cookie_credentials_are_rejected()
    {
        await using var app = new SecurityApplication();
        using var client = app.CreateSecureClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Add("Authorization", "Bearer invalid");
        request.Headers.Add("Cookie", "__Host-auth-access=invalid");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("credential_conflict", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unavailable_session_store_returns_503_without_clearing_cookies()
    {
        await using var app = new SecurityApplication();
        using var client = app.CreateSecureClient();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)) { KeyId = "test-key" };
        var token = new JwtSecurityToken("ModularMonolith", "ModularMonolith",
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("sid", Guid.NewGuid().ToString()),
                new Claim("session_version", "1"), new Claim("tenant_id", "tenant1")],
            DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Add("Cookie", "__Host-auth-access=" + new JwtSecurityTokenHandler().WriteToken(token));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("/users/")]
    [InlineData("/orders/")]
    [InlineData("/products/")]
    [InlineData("/datasources/")]
    public async Task Business_endpoints_reject_anonymous_requests_before_resolving_data(string path)
    {
        await using var app = new SecurityApplication();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class SecurityApplication(string environmentName = "Development") : WebApplicationFactory<Program>
    {
        public HttpClient CreateSecureClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = TestKey,
                    ["Jwt:KeyId"] = "test-key",
                    ["ConnectionStrings:AuthConnection"] = "Server=127.0.0.1,1;Database=Unused;User Id=test;Password=unused;Connect Timeout=1;TrustServerCertificate=True",
                    ["Auth:FrontendUrl"] = "https://localhost",
                    ["Auth:DefaultTenantId"] = "tenant1",
                    ["Auth:Tenants:0:Id"] = "tenant1",
                    ["Auth:Tenants:0:Name"] = "Tenant One",
                    ["DataProtection:ApplicationName"] = "ModularMonolith.HttpTests",
                    ["DataProtection:KeyDirectory"] = Path.Combine(Path.GetTempPath(), "modular-monolith-http-tests", Guid.NewGuid().ToString("N")),
                    ["Smtp:Host"] = "localhost",
                    ["Smtp:FromAddress"] = "test@example.test",
                    ["Cors:AllowedOrigins:0"] = "https://allowed.example.test"
                }));
            return base.CreateHost(builder);
        }
    }
}