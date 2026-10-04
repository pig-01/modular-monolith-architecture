using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.RateLimiting;
using Auth.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        var auth = configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
        if (!Uri.TryCreate(auth.FrontendUrl, UriKind.Absolute, out var frontend)
            || frontend.Scheme != "https" || !string.IsNullOrEmpty(frontend.Query) || !string.IsNullOrEmpty(frontend.Fragment)
            || auth.Tenants.Count == 0 || !auth.Tenants.Any(x => x.Id == auth.DefaultTenantId)
            || auth.Tenants.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Id.Length > 100 || string.IsNullOrWhiteSpace(x.Name))
            || auth.Tenants.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != auth.Tenants.Count)
            throw new OptionsValidationException("Auth", typeof(AuthOptions), ["Provide an HTTPS frontend URL and unique tenants including the default tenant."]);
        services.AddSingleton<IOptions<AuthOptions>>(Options.Create(auth));
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        jwt.Validate();
        services.AddSingleton<IOptions<JwtOptions>>(Options.Create(jwt));
        // Parse inside our change callback instead of IConfigurationBinder: malformed reloads
        // become invalid values and the policy retains the last known good snapshot.
        var lifetimeSection = configuration.GetSection("Auth:Lifetimes");
        services.AddOptions<AuthLifetimeOptions>().Configure(value =>
        {
            value.AccessTokenMinutes = ReadLifetime(lifetimeSection, "AccessTokenMinutes", 15);
            value.SessionDays = ReadLifetime(lifetimeSection, "SessionDays", 7);
            value.RefreshReplaySeconds = ReadLifetime(lifetimeSection, "RefreshReplaySeconds", 30);
        });
        services.AddSingleton<IOptionsChangeTokenSource<AuthLifetimeOptions>>(
            new ConfigurationChangeTokenSource<AuthLifetimeOptions>(lifetimeSection));
        services.AddSingleton<AuthLifetimePolicy>();
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAuthEmailSender, SmtpAuthEmailSender>();
        services.AddScoped<AuthService>();
        services.AddScoped<AuthSessionValidator>();
        services.AddScoped<AuthSeedService>();
        services.AddSingleton<AuthTokenIssuer>();
        services.AddScoped<CookieAntiforgeryFilter>();
        services.AddScoped<AuthAntiforgeryFilter>();
        services.AddHostedService<AuthMaintenanceService>();
        services.AddDbContext<AuthDbContext>(builder => builder.UseSqlServer(
            configuration.GetConnectionString("AuthConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:AuthConnection is required.")));

        services.AddIdentityCore<AuthAccount>(identity =>
        {
            identity.User.RequireUniqueEmail = true;
            identity.Password.RequiredLength = 15;
            identity.Password.RequireDigit = false;
            identity.Password.RequireNonAlphanumeric = false;
            identity.Password.RequireUppercase = false;
            identity.Password.RequireLowercase = false;
            identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            identity.Lockout.MaxFailedAccessAttempts = 5;
            identity.Lockout.AllowedForNewUsers = true;
            identity.SignIn.RequireConfirmedEmail = true;
            identity.Tokens.EmailConfirmationTokenProvider = "AuthEmail";
            identity.Tokens.PasswordResetTokenProvider = "AuthPasswordReset";
        }).AddEntityFrameworkStores<AuthDbContext>()
            .AddTokenProvider<EmailConfirmationTokenProvider>("AuthEmail")
            .AddTokenProvider<PasswordResetTokenProvider>("AuthPasswordReset");

        var dataProtection = configuration.GetSection("DataProtection").Get<AuthDataProtectionOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(dataProtection.ApplicationName) || string.IsNullOrWhiteSpace(dataProtection.KeyDirectory))
            throw new InvalidOperationException("Data Protection application name and key directory are required.");
        var dp = services.AddDataProtection().SetApplicationName(dataProtection.ApplicationName)
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90))
            .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(dataProtection.KeyDirectory, environment.ContentRootPath)));
        if (!string.IsNullOrWhiteSpace(dataProtection.CertificatePath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(dataProtection.CertificatePath,
                dataProtection.CertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey)
                throw new InvalidOperationException("Data Protection certificate requires a private key.");
            dp.ProtectKeysWithCertificate(certificate);
            var decryptingCertificates = dataProtection.PreviousCertificates.Select(previous =>
            {
                var oldCertificate = X509CertificateLoader.LoadPkcs12FromFile(previous.Path,
                    previous.Password, X509KeyStorageFlags.EphemeralKeySet);
                if (!oldCertificate.HasPrivateKey)
                    throw new InvalidOperationException("Previous Data Protection certificates require private keys.");
                return oldCertificate;
            }).Prepend(certificate).ToArray();
            dp.UnprotectKeysWithAnyCertificate(decryptingCertificates);
        }
        else if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("A Data Protection encryption certificate is required outside development.");
        }

        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = AuthCookies.CsrfHeader;
            antiforgery.Cookie.Name = AuthCookies.Antiforgery;
            antiforgery.Cookie.HttpOnly = true;
            antiforgery.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            antiforgery.Cookie.SameSite = SameSiteMode.Lax;
            antiforgery.Cookie.Path = "/";
        });
        var keys = jwt.PreviousKeys.Select(x => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(x.Key)) { KeyId = x.Id })
            .Prepend(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)) { KeyId = jwt.KeyId }).ToArray();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(bearer =>
        {
            bearer.MapInboundClaims = false;
            bearer.SaveToken = false;
            bearer.IncludeErrorDetails = false;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                ClockSkew = TimeSpan.Zero,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                NameClaimType = "sub",
                IssuerSigningKeyResolver = (_, _, kid, _) => keys.Where(x => x.KeyId == kid)
            };
            bearer.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.Request.Cookies.TryGetValue(AuthCookies.Access, out var cookie))
                        context.Token = cookie;
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    try
                    {
                        await context.HttpContext.RequestServices.GetRequiredService<AuthSessionValidator>()
                            .ValidateAsync(context.Principal!, context.HttpContext.RequestAborted);
                    }
                    catch (AuthProblem)
                    {
                        context.Fail("session_invalid");
                    }
                },
                OnAuthenticationFailed = context =>
                {
                    // Authentication handlers normally swallow exceptions. Storage errors must stay 503.
                    if (context.Exception is System.Data.Common.DbException or DbUpdateException or TimeoutException)
                        throw new AuthProblem(503, "service_unavailable", "服務暫時無法使用，請稍後重試。");
                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    await AuthExceptionMiddleware.WriteProblemAsync(context.HttpContext, 401, "session_invalid", "請先登入。");
                },
                OnForbidden = context => AuthExceptionMiddleware.WriteProblemAsync(context.HttpContext,
                    403, "forbidden", "您沒有執行此操作的權限。")
            };
        });
        services.AddAuthorization(authorization => authorization.AddPolicy("PlatformAdmin", policy =>
            policy.RequireAuthenticatedUser().RequireClaim("platform_admin", "true")));
        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            limiter.AddPolicy("auth-email", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            limiter.OnRejected = async (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await AuthExceptionMiddleware.WriteProblemAsync(context.HttpContext, 429, "rate_limited", "操作過於頻繁，請稍後再試。");
            };
        });
        return services;
    }

    private static int ReadLifetime(IConfiguration section, string key, int defaultValue) => section[key] is not { } text
        ? defaultValue : int.TryParse(text, out var value) ? value : -1;
}