using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application;

public sealed class AuthOptions
{
    public string FrontendUrl { get; set; } = "https://localhost:5173";
    public string DefaultTenantId { get; set; } = "tenant1";
    public List<TenantProfile> Tenants { get; set; } = [];
}

public sealed class AuthLifetimeOptions
{
    public int AccessTokenMinutes { get; set; } = 15;
    public int SessionDays { get; set; } = 7;
    public int RefreshReplaySeconds { get; set; } = 30;

    public bool IsValid() => AccessTokenMinutes is >= 1 and <= 60 && SessionDays is >= 1 and <= 30
        && RefreshReplaySeconds is >= 1 and <= 120;
}

/// <summary>Invalid reloads never replace a working lifetime configuration.</summary>
public sealed class AuthLifetimePolicy : IDisposable
{
    private AuthLifetimeOptions current;
    private readonly IDisposable? subscription;

    public AuthLifetimePolicy(IOptionsMonitor<AuthLifetimeOptions> options, ILogger<AuthLifetimePolicy> logger)
    {
        current = Copy(options.CurrentValue);
        if (!current.IsValid())
            throw new OptionsValidationException("Auth:Lifetimes", typeof(AuthLifetimeOptions), ["Invalid token lifetimes."]);
        subscription = options.OnChange(value =>
        {
            if (value.IsValid())
                Volatile.Write(ref current, Copy(value));
            else
                logger.LogError("Auth lifetime configuration reload rejected; keeping the last valid configuration.");
        });
    }

    public AuthLifetimeOptions Current => Copy(Volatile.Read(ref current));
    public void Dispose() => subscription?.Dispose();

    private static AuthLifetimeOptions Copy(AuthLifetimeOptions value) => new()
    {
        AccessTokenMinutes = value.AccessTokenMinutes,
        SessionDays = value.SessionDays,
        RefreshReplaySeconds = value.RefreshReplaySeconds
    };
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "ModularMonolith";
    public string Audience { get; set; } = "ModularMonolith";
    public string Key { get; set; } = "";
    public string KeyId { get; set; } = "current";
    public List<JwtPreviousKey> PreviousKeys { get; set; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience)
            || string.IsNullOrWhiteSpace(KeyId) || Encoding.UTF8.GetByteCount(Key) < 32
            || PreviousKeys.Any(x => string.IsNullOrWhiteSpace(x.Id) || Encoding.UTF8.GetByteCount(x.Key) < 32)
            || PreviousKeys.Select(x => x.Id).Append(KeyId).Distinct(StringComparer.Ordinal).Count() != PreviousKeys.Count + 1)
            throw new OptionsValidationException("Jwt", typeof(JwtOptions), ["Provide issuer, audience and uniquely named signing keys of at least 32 bytes."]);
    }
}

public sealed class JwtPreviousKey
{
    public string Id { get; set; } = "";
    public string Key { get; set; } = "";
}

public sealed class AuthDataProtectionOptions
{
    public string ApplicationName { get; set; } = "ModularMonolith.Auth.Development";
    public string KeyDirectory { get; set; } = ".keys/auth";
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
    public List<DataProtectionCertificate> PreviousCertificates { get; set; } = [];
}

public sealed class DataProtectionCertificate
{
    public string Path { get; set; } = "";
    public string? Password { get; set; }
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool EnableSsl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "noreply@example.test";
    public string FromName { get; set; } = "Modular Monolith";
}