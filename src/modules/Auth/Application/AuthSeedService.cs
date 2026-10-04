using Auth.Domain;
using Auth.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Auth.Application;

public sealed class AuthSeedService(AuthDbContext db, UserManager<AuthAccount> users, IOptions<AuthOptions> options)
{
    public async Task EnsureAdministratorAsync(string email, string password, string name,
        IEnumerable<string> tenantIds, CancellationToken cancellationToken = default)
    {
        new RegisterRequest(name, email, password).Validate();
        var ids = ValidateTenants(tenantIds);
        var account = await users.FindByEmailAsync(email);
        if (account is null)
        {
            account = new AuthAccount
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                Name = name,
                EmailConfirmed = true,
                IsPlatformAdmin = true,
                LockoutEnabled = true
            };
            var result = await users.CreateAsync(account, password);
            if (!result.Succeeded)
                throw new InvalidOperationException("Administrator creation failed: "
                    + string.Join(", ", result.Errors.Select(x => x.Code)));
        }
        await using var transaction = await AuthAccountLock.AcquireAsync(db, account.Id, cancellationToken);
        await db.Entry(account).ReloadAsync(cancellationToken);
        if (!account.IsPlatformAdmin)
            throw new InvalidOperationException("Administrator seed refused: this email belongs to a non-administrator account. "
                + "Use a new administrator email or an explicit, separately authorized administration procedure.");
        account.IsPlatformAdmin = true;
        account.EmailConfirmed = true;
        // Repeated seed deliberately preserves existing passwords and disabled-account status.
        foreach (var tenantId in ids)
            await GrantAsync(account.Id, tenantId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task GrantMembershipAsync(string email, string tenantId, CancellationToken cancellationToken = default)
    {
        ValidateTenants([tenantId]);
        var account = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException("The account must be registered before granting membership.");
        await using var transaction = await AuthAccountLock.AcquireAsync(db, account.Id, cancellationToken);
        await GrantAsync(account.Id, tenantId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task GrantAsync(Guid accountId, string tenantId, CancellationToken cancellationToken)
    {
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == accountId && x.TenantId == tenantId,
            cancellationToken);
        if (membership is null)
            db.Memberships.Add(new TenantMembership { AccountId = accountId, TenantId = tenantId });
        else
            membership.IsActive = true;
    }

    private string[] ValidateTenants(IEnumerable<string> ids)
    {
        var tenants = ids.Distinct(StringComparer.Ordinal).ToArray();
        if (tenants.Length == 0 || tenants.Any(id => !options.Value.Tenants.Any(t => t.Id == id)))
            throw new InvalidOperationException("Seed requires at least one configured tenant.");
        return tenants;
    }
}