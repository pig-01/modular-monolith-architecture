using System.Globalization;
using System.Security.Claims;
using Auth.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Auth.Application;

public sealed class AuthSessionValidator(AuthDbContext db, TimeProvider time, IOptions<AuthOptions> options)
{
    public async Task ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var accountId)
            || !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId)
            || !long.TryParse(principal.FindFirstValue("session_version"), NumberStyles.None,
                CultureInfo.InvariantCulture, out var version))
            throw AuthProblem.InvalidSession();

        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        var now = time.GetUtcNow();
        if (session is null || session.AccountId != accountId || session.RevokedAt is not null
            || session.ExpiresAt <= now || session.Version != version
            || session.TenantId != principal.FindFirstValue("tenant_id"))
            throw AuthProblem.InvalidSession();

        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account is null || !account.IsActive || !account.EmailConfirmed || account.SecurityStamp != session.SecurityStamp
            || account.LockoutEnd > now || !options.Value.Tenants.Any(x => x.Id == session.TenantId)
            || !await db.Memberships.AsNoTracking().AnyAsync(x => x.AccountId == accountId
                && x.TenantId == session.TenantId && x.IsActive, cancellationToken))
            throw AuthProblem.InvalidSession();

        // Authorization derives from current storage, never stale privilege claims in the JWT.
        if (principal.Identity is ClaimsIdentity identity)
        {
            foreach (var claim in identity.FindAll("platform_admin").ToArray())
                identity.RemoveClaim(claim);
            if (account.IsPlatformAdmin)
                identity.AddClaim(new Claim("platform_admin", "true"));
        }
    }
}