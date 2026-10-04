using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Auth.Application;
using Auth.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Auth.Tests;

public sealed class SqlAuthFactAttribute : FactAttribute
{
    public SqlAuthFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTH_TEST_SQL_CONNECTION")))
            Skip = "Set AUTH_TEST_SQL_CONNECTION to run real SQL Server lifecycle and concurrency tests.";
    }
}

[CollectionDefinition("SQL auth", DisableParallelization = true)]
public sealed class SqlAuthCollection : ICollectionFixture<SqlAuthFixture>
{
}

public sealed class SqlAuthFixture : IAsyncLifetime
{
    public const string Password = "a sufficiently long password";
    public ServiceProvider Services { get; private set; } = null!;
    public CapturingEmailSender Email { get; } = new();
    public MutableTimeProvider Time { get; } = new();
    public IConfigurationRoot Configuration { get; private set; } = null!;
    private string? connection;

    public async Task InitializeAsync()
    {
        var value = Environment.GetEnvironmentVariable("AUTH_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(value))
            return;
        var builder = new SqlConnectionStringBuilder(value)
        {
            InitialCatalog = "AuthModuleTests_" + Guid.NewGuid().ToString("N")
        };
        connection = builder.ConnectionString;
        Configuration = BuildConfiguration(connection);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthModule(Configuration, new TestEnvironment());
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time));
        services.Replace(ServiceDescriptor.Singleton<IAuthEmailSender>(Email));
        Services = services.BuildServiceProvider();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (connection is null)
            return;
        await using var scope = Services.CreateAsyncScope();
        // The name is generated above for this fixture; never delete the database supplied by the caller.
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureDeletedAsync();
        await Services.DisposeAsync();
    }

    public async Task<T> UseAsync<T>(Func<AuthService, Task<T>> operation)
    {
        await using var scope = Services.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<AuthService>());
    }

    public async Task UseAsync(Func<AuthService, Task> operation) => await UseAsync(async auth =>
    {
        await operation(auth);
        return true;
    });

    public async Task<(string Email, SessionTokens Tokens)> CreateSessionAsync()
    {
        var address = Guid.NewGuid().ToString("N") + "@example.test";
        await UseAsync(auth => auth.RegisterAsync(new RegisterRequest("測試使用者", address, Password), default));
        var link = Email.Link(address);
        await UseAsync(auth => auth.ConfirmEmailAsync(new ConfirmEmailRequest(link.UserId, link.Token), default));
        return (address, await UseAsync(auth => auth.LoginAsync(new LoginRequest(address, Password), default)));
    }

    public static ClaimsPrincipal Principal(SessionTokens tokens) => new(new ClaimsIdentity(
        new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken).Claims, "Bearer"));

    public static IConfigurationRoot BuildConfiguration(string connection = "Server=unused;Database=unused") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AuthConnection"] = connection,
            ["Auth:FrontendUrl"] = "https://localhost:5173",
            ["Auth:DefaultTenantId"] = "tenant1",
            ["Auth:Tenants:0:Id"] = "tenant1",
            ["Auth:Tenants:0:Name"] = "預設租戶",
            ["Auth:Tenants:1:Id"] = "tenant2",
            ["Auth:Tenants:1:Name"] = "第二租戶",
            ["Jwt:Issuer"] = "AuthTests",
            ["Jwt:Audience"] = "AuthTests",
            ["Jwt:Key"] = "test-only-signing-key-32-byte-minimum-auth-tests",
            ["Jwt:KeyId"] = "test",
            ["DataProtection:ApplicationName"] = "AuthTests",
            ["DataProtection:KeyDirectory"] = Path.Combine(Path.GetTempPath(), "ModularMonolithAuthTestsKeys")
        }).Build();
}

public sealed class MutableTimeProvider : TimeProvider
{
    private long offsetTicks;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref offsetTicks));
    public void Advance(TimeSpan offset) => Interlocked.Add(ref offsetTicks, offset.Ticks);
    public void Reset() => Interlocked.Exchange(ref offsetTicks, 0);
}

public sealed class CapturingEmailSender : IAuthEmailSender
{
    private readonly ConcurrentDictionary<string, string> messages = new();
    public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        messages[recipient] = body;
        return Task.CompletedTask;
    }

    public (Guid UserId, string Token) Link(string email)
    {
        var fragment = new Uri(messages[email].Split('\n').Last()).Fragment.TrimStart('#');
        var fields = fragment.Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
        return (Guid.Parse(fields["userId"]), fields["token"]);
    }
}

public sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "Auth.Tests";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}