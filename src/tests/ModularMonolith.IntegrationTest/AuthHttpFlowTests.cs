using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auth.Application;
using Auth.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ModularMonolith.IntegrationTest;

public sealed class SqlAuthFactAttribute : FactAttribute
{
    public SqlAuthFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTH_TEST_SQL_CONNECTION")))
            Skip = "Set AUTH_TEST_SQL_CONNECTION to an isolated SQL Server for HTTP authentication tests.";
    }
}

public class AuthHttpFlowTests
{
    [SqlAuthFact]
    public async Task A_registered_user_can_verify_login_switch_tenant_and_revoke_the_session()
    {
        await using var app = new AuthApplication();
        await app.InitializeDatabaseAsync();
        using var client = app.CreateSecureClient();
        const string email = "member@example.test";
        const string password = "A long password for the HTTP test";
        using var registered = await PostAsync(client, "register", new { name = "測試使用者", email, password });
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        var link = app.Mail.LatestLink(email);

        using var unverified = await PostAsync(client, "login", new { email, password });
        Assert.Equal(HttpStatusCode.Forbidden, unverified.StatusCode);
        using var confirmed = await PostAsync(client, "confirm-email", link);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        using var loggedIn = await PostAsync(client, "login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        var profile = await loggedIn.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tenant1", profile.GetProperty("currentTenant").GetProperty("id").GetString());
        Assert.False(profile.TryGetProperty("accessToken", out _));
        Assert.False(profile.TryGetProperty("refreshToken", out _));
        var accessCookie = loggedIn.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-auth-access="));
        Assert.Contains("httponly", accessCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", accessCookie, StringComparison.OrdinalIgnoreCase);
        var oldAccess = accessCookie.Split(';')[0].Split('=', 2)[1];

        using var deniedBusiness = await client.GetAsync("/users/");
        Assert.Equal(HttpStatusCode.Forbidden, deniedBusiness.StatusCode);
        using var deniedTenant = await PostAsync(client, "switch-tenant", new { tenantId = "tenant2", requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, deniedTenant.StatusCode);
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AuthSeedService>().GrantMembershipAsync(email, "tenant2");
        using var switched = await PostAsync(client, "switch-tenant", new { tenantId = "tenant2", requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var switchedProfile = await switched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tenant2", switchedProfile.GetProperty("currentTenant").GetProperty("id").GetString());

        using var oldTokenClient = app.CreateSecureClient();
        oldTokenClient.DefaultRequestHeaders.Authorization = new("Bearer", oldAccess);
        using var oldTenant = await oldTokenClient.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, oldTenant.StatusCode);

        using var refreshed = await PostAsync(client, "refresh", new { requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var access = refreshed.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-auth-access="))
            .Split(';')[0].Split('=', 2)[1];
        var refreshCookie = refreshed.Headers.GetValues("Set-Cookie")
            .Single(x => x.StartsWith("__Host-auth-refresh=")).Split(';')[0];
        // Browsers stop sending the access cookie when it expires. The refresh cookie must still permit logout.
        using var refreshOnlyClient = app.CreateSecureClient();
        refreshOnlyClient.DefaultRequestHeaders.Add("Cookie", refreshCookie);
        using var loggedOut = await PostAsync(refreshOnlyClient, "logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);
        Assert.Equal(2, loggedOut.Headers.GetValues("Set-Cookie").Count());
        using var anonymous = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        oldTokenClient.DefaultRequestHeaders.Authorization = new("Bearer", access);
        using var copiedToken = await oldTokenClient.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, copiedToken.StatusCode);
    }

    [SqlAuthFact]
    public async Task Password_reset_revokes_all_devices_and_allows_only_the_new_password()
    {
        await using var app = new AuthApplication();
        await app.InitializeDatabaseAsync();
        using var first = app.CreateSecureClient();
        using var second = app.CreateSecureClient();
        const string email = "reset@example.test";
        const string password = "The first long password for tests";
        using var registered = await PostAsync(first, "register", new { name = "Reset Test", email, password });
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        using var confirmed = await PostAsync(first, "confirm-email", app.Mail.LatestLink(email));
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        using var login1 = await PostAsync(first, "login", new { email, password });
        using var login2 = await PostAsync(second, "login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login2.StatusCode);
        using var forgot = await PostAsync(first, "forgot-password", new { email });
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        var link = app.Mail.LatestLink(email);
        const string replacement = "A completely different long password";
        using var reset = await PostAsync(first, "reset-password", new { link.UserId, link.Token, password = replacement });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        using var invalidated = await second.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, invalidated.StatusCode);
        using var oldPassword = await PostAsync(second, "login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using var newPassword = await PostAsync(second, "login", new { email, password = replacement });
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string action, object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/" + action)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        return await client.SendAsync(request);
    }

    private sealed class AuthApplication : WebApplicationFactory<Program>
    {
        private readonly string connection;
        private bool databaseInitialized;
        public TestMail Mail { get; } = new();

        public AuthApplication()
        {
            var settings = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUTH_TEST_SQL_CONNECTION"));
            settings.InitialCatalog = "ModularMonolithHttpTests_" + Guid.NewGuid().ToString("N");
            connection = settings.ConnectionString;
        }

        public HttpClient CreateSecureClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        public async Task InitializeDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
            databaseInitialized = true;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IAuthEmailSender>(Mail)));
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuthConnection"] = connection,
                ["Jwt:Key"] = "http-flow-test-only-key-0123456789abcdef-not-a-deployed-secret",
                ["Jwt:KeyId"] = "flow-test",
                ["Auth:FrontendUrl"] = "https://localhost",
                ["Auth:DefaultTenantId"] = "tenant1",
                ["Auth:Tenants:0:Id"] = "tenant1",
                ["Auth:Tenants:0:Name"] = "Tenant One",
                ["Auth:Tenants:1:Id"] = "tenant2",
                ["Auth:Tenants:1:Name"] = "Tenant Two",
                ["DataProtection:ApplicationName"] = "ModularMonolith.HttpFlowTests",
                ["DataProtection:KeyDirectory"] = Path.Combine(Path.GetTempPath(), "modular-monolith-flow-tests", Guid.NewGuid().ToString("N"))
            }));
            return base.CreateHost(builder);
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (databaseInitialized)
                {
                    using var scope = Services.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureDeletedAsync();
                }
            }
            finally
            {
                await base.DisposeAsync();
            }
        }
    }

    private sealed class TestMail : IAuthEmailSender
    {
        private readonly ConcurrentDictionary<string, string> messages = new();
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
        {
            messages[recipient] = body;
            return Task.CompletedTask;
        }

        public ConfirmationLink LatestLink(string recipient)
        {
            var uri = new Uri(messages[recipient].Split('\n').Last());
            var query = QueryHelpers.ParseQuery(uri.Fragment.TrimStart('#'));
            return new ConfirmationLink(Guid.Parse(query["userId"].ToString()), query["token"].ToString());
        }
    }

    private sealed record ConfirmationLink(Guid UserId, string Token);
}