using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Auth.Domain;
using Auth.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application;

public sealed class AuthService(
    AuthDbContext db,
    UserManager<AuthAccount> users,
    AuthTokenIssuer issuer,
    AuthLifetimePolicy lifetimes,
    IOptions<AuthOptions> options,
    IDataProtectionProvider protection,
    IAuthEmailSender email,
    TimeProvider time,
    ILogger<AuthService> logger)
{
    private readonly IDataProtector replayProtector = protection.CreateProtector("Auth.RefreshReplay.v1");

    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var normalizedEmail = request.Email.Trim();
        if (await users.FindByEmailAsync(normalizedEmail) is not null)
            return;
        var account = new AuthAccount
        {
            Id = Guid.NewGuid(),
            UserName = normalizedEmail,
            Email = normalizedEmail,
            Name = request.Name.Trim(),
            LockoutEnabled = true
        };
        IdentityResult result;
        try
        {
            result = await users.CreateAsync(account, request.Password);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return;
        }
        if (!result.Succeeded)
        {
            if (result.Errors.Any(x => x.Code is "DuplicateEmail" or "DuplicateUserName"))
                return;
            ThrowIdentityErrors(result);
        }
        await SendConfirmationAsync(account, cancellationToken);
        logger.LogInformation("Auth registration accepted for account {AccountId}.", account.Id);
    }

    public async Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        await InAccountTransactionAsync(request.UserId, async () =>
        {
            var account = await users.FindByIdAsync(request.UserId.ToString()) ?? throw InvalidLink();
            if (string.IsNullOrEmpty(request.Token) || request.Token.Length > 4096)
                throw InvalidLink();
            var result = await users.ConfirmEmailAsync(account, request.Token);
            if (!result.Succeeded)
                throw InvalidLink();
            var tenantId = options.Value.DefaultTenantId;
            if (!await db.Memberships.AnyAsync(x => x.AccountId == account.Id && x.TenantId == tenantId, cancellationToken))
                db.Memberships.Add(new TenantMembership { AccountId = account.Id, TenantId = tenantId });
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task ResendConfirmationAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        ValidateEmail(request.Email);
        var account = await users.FindByEmailAsync(request.Email.Trim());
        if (account is not null && !account.EmailConfirmed && account.IsActive)
            await SendConfirmationAsync(account, cancellationToken);
    }

    public async Task ForgotPasswordAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        ValidateEmail(request.Email);
        var account = await users.FindByEmailAsync(request.Email.Trim());
        if (account is not null && account.EmailConfirmed && account.IsActive)
        {
            var token = await users.GeneratePasswordResetTokenAsync(account);
            await SendEmailSafelyAsync(account, "重設密碼", "請在 30 分鐘內開啟連結重設密碼：\n"
                + BuildEmailLink("reset-password", account.Id, token), cancellationToken);
        }
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        RequestValidation.Password(request.Password, errors);
        RequestValidation.ThrowIfInvalid(errors);
        await InAccountTransactionAsync(request.UserId, async () =>
        {
            var account = await users.FindByIdAsync(request.UserId.ToString()) ?? throw InvalidLink();
            if (!account.IsActive || !account.EmailConfirmed || string.IsNullOrEmpty(request.Token) || request.Token.Length > 4096)
                throw InvalidLink();
            var result = await users.ResetPasswordAsync(account, request.Token, request.Password);
            if (!result.Succeeded)
                throw InvalidLink();
            await users.ResetAccessFailedCountAsync(account);
            await users.SetLockoutEndDateAsync(account, null);
            var now = time.GetUtcNow();
            await db.Sessions.Where(x => x.AccountId == account.Id && x.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAt, now), cancellationToken);
            await db.RefreshTokens.Where(x => db.Sessions.Any(s => s.Id == x.SessionId && s.AccountId == account.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.ProtectedReplay, (string?)null), cancellationToken);
            logger.LogInformation("Password reset revoked all sessions for account {AccountId}.", account.Id);
            return true;
        }, cancellationToken);
    }

    public async Task<SessionTokens> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        // Bound hashing inputs before reaching the expensive password verifier.
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254
            || string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            throw InvalidCredentials();
        var candidate = await users.FindByEmailAsync(request.Email.Trim());
        if (candidate is null)
        {
            // Keep the unknown-account path on the same password-hashing work factor.
            users.PasswordHasher.HashPassword(new AuthAccount(), request.Password);
            throw InvalidCredentials();
        }
        return await InAccountTransactionAsync(candidate.Id, async () =>
        {
            var account = await users.FindByIdAsync(candidate.Id.ToString()) ?? throw InvalidCredentials();
            if (!account.IsActive || await users.IsLockedOutAsync(account))
                throw InvalidCredentials();
            if (!await users.CheckPasswordAsync(account, request.Password))
            {
                await users.AccessFailedAsync(account);
                logger.LogInformation("Login rejected for account {AccountId}.", account.Id);
                throw new AuthProblem(401, "invalid_credentials", "Email 或密碼錯誤，或帳號暫時無法登入。") { CommitState = true };
            }
            if (!account.EmailConfirmed)
                throw new AuthProblem(403, "email_unconfirmed", "請先完成信箱驗證。");
            await users.ResetAccessFailedCountAsync(account);
            var tenants = await GetTenantsAsync(account.Id, cancellationToken);
            var tenant = tenants.FirstOrDefault(x => x.Id == options.Value.DefaultTenantId) ?? tenants.FirstOrDefault()
                ?? throw new AuthProblem(403, "tenant_forbidden", "帳號目前沒有可使用的租戶。");
            var now = time.GetUtcNow();
            var session = new AuthSession
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                TenantId = tenant.Id,
                SecurityStamp = account.SecurityStamp!,
                CreatedAt = now,
                ExpiresAt = now.AddDays(lifetimes.Current.SessionDays)
            };
            db.Sessions.Add(session);
            var pair = CreatePair(account, session, tenants, now);
            db.RefreshTokens.Add(NewRecord(session, pair.RefreshToken));
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Login created session {SessionId} for account {AccountId}.", session.Id, account.Id);
            return pair;
        }, cancellationToken);
    }

    public async Task<SessionTokens> RefreshAsync(string? refreshToken, Guid requestId, CancellationToken cancellationToken)
    {
        RequestValidation.Operation(requestId);
        var (hash, accountId) = await FindRefreshAccountAsync(refreshToken, cancellationToken);
        return await InAccountTransactionAsync(accountId, async () =>
        {
            var token = await db.RefreshTokens.SingleAsync(x => x.TokenHash == hash, cancellationToken);
            var session = await db.Sessions.SingleAsync(x => x.Id == token.SessionId, cancellationToken);
            var account = await GetValidAccountAsync(session, cancellationToken);
            var now = time.GetUtcNow();
            if (token.SessionVersion != session.Version || token.ExpiresAt <= now)
                return await RevokeReplayAsync(session, now, cancellationToken);

            // A response can reach the cookie jar while its body is lost. Accept the same operation
            // with either the predecessor cookie or its still-current successor, without rotating twice.
            RefreshTokenRecord? replay = token.ConsumedAt is not null ? token
                : await db.RefreshTokens.SingleOrDefaultAsync(x => x.SessionId == session.Id
                    && x.RequestId == requestId && x.SuccessorHash == hash, cancellationToken);
            if (replay is not null)
            {
                if (replay.RequestId != requestId || replay.SessionVersion != session.Version
                    || replay.ReplayUntil <= now || replay.ProtectedReplay is null)
                    return await RevokeReplayAsync(session, now, cancellationToken);
                var successor = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == replay.SuccessorHash,
                    cancellationToken);
                if (successor is null || successor.ConsumedAt is not null || successor.SessionVersion != session.Version)
                    return await RevokeReplayAsync(session, now, cancellationToken);
                try
                {
                    var pair = JsonSerializer.Deserialize<SessionTokens>(replayProtector.Unprotect(replay.ProtectedReplay))
                        ?? throw new CryptographicException();
                    if (pair.Profile.AccessExpiresAt <= now)
                        return await RevokeReplayAsync(session, now, cancellationToken);
                    return pair;
                }
                catch (CryptographicException)
                {
                    return await RevokeReplayAsync(session, now, cancellationToken);
                }
            }
            if (await db.RefreshTokens.AnyAsync(x => x.SessionId == session.Id && x.RequestId == requestId,
                cancellationToken))
                return await RevokeReplayAsync(session, now, cancellationToken);

            var tenants = await GetTenantsAsync(account.Id, cancellationToken);
            var result = CreatePair(account, session, tenants, now);
            token.ConsumedAt = now;
            token.RequestId = requestId;
            token.SuccessorHash = AuthTokenIssuer.Hash(result.RefreshToken);
            token.ReplayUntil = now.AddSeconds(lifetimes.Current.RefreshReplaySeconds);
            token.ProtectedReplay = replayProtector.Protect(JsonSerializer.Serialize(result));
            db.RefreshTokens.Add(NewRecord(session, result.RefreshToken));
            await db.SaveChangesAsync(cancellationToken);
            return result;
        }, cancellationToken);
    }

    public async Task<SessionTokens> SwitchTenantAsync(ClaimsPrincipal principal, SwitchTenantRequest request,
        CancellationToken cancellationToken)
    {
        RequestValidation.Operation(request.RequestId);
        var (accountId, sessionId, version) = ParsePrincipal(principal);
        return await InAccountTransactionAsync(accountId, async () =>
        {
            var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.AccountId == accountId,
                cancellationToken) ?? throw AuthProblem.InvalidSession();
            var account = await GetValidAccountAsync(session, cancellationToken);
            if (session.Version != version)
                throw AuthProblem.InvalidSession();
            var tenants = await GetTenantsAsync(account.Id, cancellationToken);
            if (!tenants.Any(x => x.Id == request.TenantId))
                throw new AuthProblem(403, "tenant_forbidden", "您沒有此租戶的存取權。");
            session.TenantId = request.TenantId;
            session.Version++;
            await ClearReplayAsync(session.Id, cancellationToken);
            var pair = CreatePair(account, session, tenants, time.GetUtcNow());
            db.RefreshTokens.Add(NewRecord(session, pair.RefreshToken));
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Session {SessionId} switched to tenant {TenantId}.", session.Id, session.TenantId);
            return pair;
        }, cancellationToken);
    }

    public async Task LogoutAsync(ClaimsPrincipal principal, string? refreshToken, CancellationToken cancellationToken)
    {
        Guid accountId;
        Guid sessionId;
        if (principal.Identity?.IsAuthenticated == true)
        {
            (accountId, sessionId, _) = ParsePrincipal(principal);
        }
        else
        {
            try
            {
                var (hash, id) = await FindRefreshAccountAsync(refreshToken, cancellationToken);
                accountId = id;
                sessionId = await db.RefreshTokens.Where(x => x.TokenHash == hash).Select(x => x.SessionId)
                    .SingleAsync(cancellationToken);
            }
            catch (AuthProblem)
            {
                return;
            }
        }
        await InAccountTransactionAsync(accountId, async () =>
        {
            var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.AccountId == accountId,
                cancellationToken);
            if (session is not null)
            {
                session.RevokedAt ??= time.GetUtcNow();
                await ClearReplayAsync(sessionId, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Session {SessionId} logged out.", sessionId);
            }
            return true;
        }, cancellationToken);
    }

    public async Task<SessionProfile> GetProfileAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var (accountId, sessionId, version) = ParsePrincipal(principal);
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId && x.AccountId == accountId,
            cancellationToken) ?? throw AuthProblem.InvalidSession();
        var account = await GetValidAccountAsync(session, cancellationToken);
        if (version != session.Version || !long.TryParse(principal.FindFirstValue("exp"), out var expires))
            throw AuthProblem.InvalidSession();
        return Profile(account, session, await GetTenantsAsync(account.Id, cancellationToken),
            DateTimeOffset.FromUnixTimeSeconds(expires));
    }

    private async Task<AuthAccount> GetValidAccountAsync(AuthSession session, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var account = await db.Users.SingleOrDefaultAsync(x => x.Id == session.AccountId, cancellationToken);
        if (session.RevokedAt is not null || session.ExpiresAt <= now || account is null || !account.IsActive
            || !account.EmailConfirmed || account.SecurityStamp != session.SecurityStamp || account.LockoutEnd > now
            || !options.Value.Tenants.Any(x => x.Id == session.TenantId)
            || !await db.Memberships.AnyAsync(x => x.AccountId == session.AccountId && x.TenantId == session.TenantId
                && x.IsActive, cancellationToken))
            throw AuthProblem.InvalidSession();
        return account;
    }

    private async Task<(string Hash, Guid AccountId)> FindRefreshAccountAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 256)
            throw AuthProblem.InvalidSession();
        var hash = AuthTokenIssuer.Hash(token);
        var accountId = await (from refresh in db.RefreshTokens.AsNoTracking()
                               join session in db.Sessions.AsNoTracking() on refresh.SessionId equals session.Id
                               where refresh.TokenHash == hash
                               select (Guid?)session.AccountId).SingleOrDefaultAsync(cancellationToken);
        return accountId is null ? throw AuthProblem.InvalidSession() : (hash, accountId.Value);
    }

    private async Task<SessionTokens> RevokeReplayAsync(AuthSession session, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        session.RevokedAt = now;
        await ClearReplayAsync(session.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Refresh replay revoked session {SessionId}.", session.Id);
        throw new AuthProblem(401, "session_invalid", "登入已失效，請重新登入。") { CommitState = true };
    }

    private Task<int> ClearReplayAsync(Guid sessionId, CancellationToken cancellationToken) =>
        db.RefreshTokens.Where(x => x.SessionId == sessionId && x.ProtectedReplay != null)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.ProtectedReplay, (string?)null), cancellationToken);

    private async Task<T> InAccountTransactionAsync<T>(Guid accountId, Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var transaction = await AuthAccountLock.AcquireAsync(db, accountId, cancellationToken);
        db.ChangeTracker.Clear();
        try
        {
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (AuthProblem problem) when (problem.CommitState)
        {
            await transaction.CommitAsync(cancellationToken);
            throw;
        }
    }

    private SessionTokens CreatePair(AuthAccount account, AuthSession session, IReadOnlyList<TenantProfile> tenants,
        DateTimeOffset now)
    {
        var expires = now.AddMinutes(lifetimes.Current.AccessTokenMinutes);
        if (expires > session.ExpiresAt)
            expires = session.ExpiresAt;
        // JWT NumericDate uses whole seconds; return the exact expiry clients will observe.
        expires = DateTimeOffset.FromUnixTimeSeconds(expires.ToUnixTimeSeconds());
        if (expires <= now)
            throw AuthProblem.InvalidSession();
        return new SessionTokens(issuer.Issue(account, session, now, expires), AuthTokenIssuer.NewRefreshToken(),
            Profile(account, session, tenants, expires));
    }

    private static RefreshTokenRecord NewRecord(AuthSession session, string token) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = session.Id,
        TokenHash = AuthTokenIssuer.Hash(token),
        SessionVersion = session.Version,
        ExpiresAt = session.ExpiresAt
    };

    private static SessionProfile Profile(AuthAccount account, AuthSession session, IReadOnlyList<TenantProfile> tenants,
        DateTimeOffset accessExpires) => new(new UserProfile(account.Id, account.Email!, account.Name),
            tenants.Single(x => x.Id == session.TenantId), tenants, account.IsPlatformAdmin, accessExpires, session.ExpiresAt);

    private async Task<IReadOnlyList<TenantProfile>> GetTenantsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var memberships = await db.Memberships.AsNoTracking().Where(x => x.AccountId == accountId && x.IsActive)
            .Select(x => x.TenantId).ToListAsync(cancellationToken);
        return options.Value.Tenants.Where(x => memberships.Contains(x.Id)).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }

    private async Task SendConfirmationAsync(AuthAccount account, CancellationToken cancellationToken)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(account);
        await SendEmailSafelyAsync(account, "驗證您的信箱", "請在 24 小時內開啟連結並確認信箱：\n"
            + BuildEmailLink("verify-email", account.Id, token), cancellationToken);
    }

    private async Task SendEmailSafelyAsync(AuthAccount account, string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            await email.SendAsync(account.Email!, subject, body, cancellationToken);
        }
        catch (SmtpException)
        {
            // Return the same public response for existing and unknown accounts. Resend can recover.
            logger.LogError("Auth email delivery failed for account {AccountId}; resend is available.", account.Id);
        }
    }

    private string BuildEmailLink(string route, Guid accountId, string token) =>
        $"{options.Value.FrontendUrl.TrimEnd('/')}/{route}#userId={accountId:D}&token={Uri.EscapeDataString(token)}";

    private static (Guid AccountId, Guid SessionId, long Version) ParsePrincipal(ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var accountId)
            || !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId)
            || !long.TryParse(principal.FindFirstValue("session_version"), NumberStyles.None,
                CultureInfo.InvariantCulture, out var version))
            throw AuthProblem.InvalidSession();
        return (accountId, sessionId, version);
    }

    private static void ValidateEmail(string email)
    {
        var errors = new Dictionary<string, string[]>();
        RequestValidation.Email(email, errors);
        RequestValidation.ThrowIfInvalid(errors);
    }

    private static void ThrowIdentityErrors(IdentityResult result) => throw new AuthProblem(400, "validation_failed",
        "請檢查輸入欄位。", new Dictionary<string, string[]> { ["password"] = result.Errors.Select(x => x.Description).ToArray() });
    private static AuthProblem InvalidCredentials() => new(401, "invalid_credentials", "Email 或密碼錯誤，或帳號暫時無法登入。");
    private static AuthProblem InvalidLink() => new(400, "invalid_token", "連結無效或已過期，請重新申請。");
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
}