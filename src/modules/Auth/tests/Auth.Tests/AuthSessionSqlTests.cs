using System.Security.Claims;
using Auth.Application;
using Auth.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Tests;

[Collection("SQL auth")]
public sealed class AuthSessionSqlTests(SqlAuthFixture fixture) : IDisposable
{
    public void Dispose() => fixture.Time.Reset();

    [SqlAuthFact]
    public async Task Administrator_seed_refuses_to_promote_an_existing_public_account()
    {
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        await fixture.UseAsync(auth => auth.RegisterAsync(new RegisterRequest("Public account", email,
            SqlAuthFixture.Password), default));
        await using var scope = fixture.Services.CreateAsyncScope();
        var problem = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<AuthSeedService>().EnsureAdministratorAsync(email,
                "administrator configured password", "Administrator", ["tenant1"]));
        Assert.Contains("non-administrator", problem.Message);
        var login = await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.LoginAsync(new LoginRequest(email, SqlAuthFixture.Password), default)));
        Assert.Equal("email_unconfirmed", login.Code);
    }

    [SqlAuthFact]
    public async Task Refresh_retry_with_old_or_successor_cookie_returns_the_same_pair_and_absolute_expiry()
    {
        var (_, original) = await fixture.CreateSessionAsync();
        var requestId = Guid.NewGuid();
        var refreshed = await fixture.UseAsync(auth => auth.RefreshAsync(original.RefreshToken, requestId, default));
        var oldCookieRetry = await fixture.UseAsync(auth => auth.RefreshAsync(original.RefreshToken, requestId, default));
        var newCookieRetry = await fixture.UseAsync(auth => auth.RefreshAsync(refreshed.RefreshToken, requestId, default));

        Assert.Equal(refreshed.AccessToken, oldCookieRetry.AccessToken);
        Assert.Equal(refreshed.RefreshToken, oldCookieRetry.RefreshToken);
        Assert.Equal(refreshed.AccessToken, newCookieRetry.AccessToken);
        Assert.Equal(refreshed.RefreshToken, newCookieRetry.RefreshToken);
        Assert.Equal(original.Profile.SessionExpiresAt, refreshed.Profile.SessionExpiresAt);
    }

    [SqlAuthFact]
    public async Task Concurrent_refresh_of_the_same_operation_rotates_exactly_once()
    {
        var (_, original) = await fixture.CreateSessionAsync();
        var requestId = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.UseAsync(auth =>
            auth.RefreshAsync(original.RefreshToken, requestId, default))));

        Assert.Single(results.Select(x => x.RefreshToken).Distinct());
        Assert.Single(results.Select(x => x.AccessToken).Distinct());
        var profile = await fixture.UseAsync(auth => auth.GetProfileAsync(SqlAuthFixture.Principal(results[0]), default));
        Assert.Equal(original.Profile.User.Id, profile.User.Id);
    }

    [SqlAuthFact]
    public async Task Different_operation_reusing_a_consumed_token_revokes_the_session()
    {
        var (_, original) = await fixture.CreateSessionAsync();
        var refreshed = await fixture.UseAsync(auth => auth.RefreshAsync(original.RefreshToken, Guid.NewGuid(), default));
        var problem = await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.RefreshAsync(original.RefreshToken, Guid.NewGuid(), default)));
        Assert.Equal("session_invalid", problem.Code);
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.GetProfileAsync(SqlAuthFixture.Principal(refreshed), default)));
    }

    [SqlAuthFact]
    public async Task Concurrent_different_refresh_operations_cannot_leave_a_valid_session()
    {
        var (_, original) = await fixture.CreateSessionAsync();
        async Task<AuthProblem?> Refresh()
        {
            try
            {
                await fixture.UseAsync(auth => auth.RefreshAsync(original.RefreshToken, Guid.NewGuid(), default));
                return null;
            }
            catch (AuthProblem problem)
            {
                return problem;
            }
        }
        var results = await Task.WhenAll(Refresh(), Refresh());
        Assert.Single(results, x => x?.Code == "session_invalid");
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.GetProfileAsync(SqlAuthFixture.Principal(original), default)));
    }

    [SqlAuthFact]
    public async Task Refresh_retry_after_the_grace_period_revokes_the_session()
    {
        var (_, original) = await fixture.CreateSessionAsync();
        var requestId = Guid.NewGuid();
        await fixture.UseAsync(auth => auth.RefreshAsync(original.RefreshToken, requestId, default));
        fixture.Time.Advance(TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.RefreshAsync(original.RefreshToken, requestId, default)));
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.GetProfileAsync(SqlAuthFixture.Principal(original), default)));
    }

    [SqlAuthFact]
    public async Task Refresh_cannot_replay_an_older_generation_after_its_successor_was_consumed()
    {
        var (_, original) = await fixture.CreateSessionAsync();
        var firstRequest = Guid.NewGuid();
        var first = await fixture.UseAsync(auth => auth.RefreshAsync(original.RefreshToken, firstRequest, default));
        var second = await fixture.UseAsync(auth => auth.RefreshAsync(first.RefreshToken, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.RefreshAsync(original.RefreshToken, firstRequest, default)));
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.GetProfileAsync(SqlAuthFixture.Principal(second), default)));
    }

    [SqlAuthFact]
    public async Task Logout_with_only_refresh_cookie_prevents_all_replay_and_invalidates_access()
    {
        var (_, original) = await fixture.CreateSessionAsync();
        var requestId = Guid.NewGuid();
        var pair = await fixture.UseAsync(auth => auth.RefreshAsync(original.RefreshToken, requestId, default));
        await fixture.UseAsync(auth => auth.LogoutAsync(new ClaimsPrincipal(), pair.RefreshToken, default));
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.RefreshAsync(original.RefreshToken, requestId, default)));
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.GetProfileAsync(SqlAuthFixture.Principal(pair), default)));
    }

    [SqlAuthFact]
    public async Task Concurrent_logout_and_refresh_cannot_restore_a_logged_out_session()
    {
        var (_, original) = await fixture.CreateSessionAsync();
        var requestId = Guid.NewGuid();
        var refresh = TryRefreshAsync(original.RefreshToken, requestId);
        var logout = fixture.UseAsync(auth => auth.LogoutAsync(SqlAuthFixture.Principal(original),
            original.RefreshToken, default));
        await Task.WhenAll(refresh, logout);
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.GetProfileAsync(SqlAuthFixture.Principal(original), default)));
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.RefreshAsync(original.RefreshToken, requestId, default)));
    }

    [SqlAuthFact]
    public async Task Concurrent_password_reset_and_refresh_cannot_restore_the_previous_security_stamp()
    {
        var (email, original) = await fixture.CreateSessionAsync();
        await fixture.UseAsync(auth => auth.ForgotPasswordAsync(new EmailRequest(email), default));
        var link = fixture.Email.Link(email);
        var requestId = Guid.NewGuid();
        var refresh = TryRefreshAsync(original.RefreshToken, requestId);
        var reset = fixture.UseAsync(auth => auth.ResetPasswordAsync(new ResetPasswordRequest(link.UserId,
            link.Token, "a different sufficiently long password"), default));
        await Task.WhenAll(refresh, reset);
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.GetProfileAsync(SqlAuthFixture.Principal(original), default)));
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.RefreshAsync(original.RefreshToken, requestId, default)));
    }

    [SqlAuthFact]
    public async Task Tenant_switch_invalidates_old_access_and_replay_while_preserving_absolute_expiry()
    {
        var (email, original) = await fixture.CreateSessionAsync();
        await using (var scope = fixture.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuthSeedService>().GrantMembershipAsync(email, "tenant2");
        var requestId = Guid.NewGuid();
        var refreshed = await fixture.UseAsync(auth => auth.RefreshAsync(original.RefreshToken, requestId, default));
        var switched = await fixture.UseAsync(auth => auth.SwitchTenantAsync(SqlAuthFixture.Principal(refreshed),
            new SwitchTenantRequest("tenant2", Guid.NewGuid()), default));

        Assert.Equal("tenant2", switched.Profile.CurrentTenant.Id);
        Assert.Equal(original.Profile.SessionExpiresAt, switched.Profile.SessionExpiresAt);
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.GetProfileAsync(SqlAuthFixture.Principal(refreshed), default)));
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.RefreshAsync(original.RefreshToken, requestId, default)));
    }

    [SqlAuthFact]
    public async Task Password_reset_revokes_every_device_and_prevents_old_refresh_replay()
    {
        var (email, original) = await fixture.CreateSessionAsync();
        var other = await fixture.UseAsync(auth => auth.LoginAsync(new LoginRequest(email, SqlAuthFixture.Password), default));
        var requestId = Guid.NewGuid();
        await fixture.UseAsync(auth => auth.RefreshAsync(original.RefreshToken, requestId, default));
        await fixture.UseAsync(auth => auth.ForgotPasswordAsync(new EmailRequest(email), default));
        var link = fixture.Email.Link(email);
        await fixture.UseAsync(auth => auth.ResetPasswordAsync(new ResetPasswordRequest(link.UserId, link.Token,
            "a different sufficiently long password"), default));

        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.RefreshAsync(original.RefreshToken, requestId, default)));
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.GetProfileAsync(SqlAuthFixture.Principal(other), default)));
        var newLogin = await fixture.UseAsync(auth => auth.LoginAsync(new LoginRequest(email,
            "a different sufficiently long password"), default));
        Assert.Equal(original.Profile.User.Id, newLogin.Profile.User.Id);
    }

    [SqlAuthFact]
    public async Task Session_absolute_expiry_cannot_be_extended_by_refreshing()
    {
        var (_, original) = await fixture.CreateSessionAsync();
        fixture.Time.Advance(TimeSpan.FromDays(6));
        var refreshed = await fixture.UseAsync(auth => auth.RefreshAsync(original.RefreshToken, Guid.NewGuid(), default));
        Assert.Equal(original.Profile.SessionExpiresAt, refreshed.Profile.SessionExpiresAt);
        fixture.Time.Advance(TimeSpan.FromDays(1));
        await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.RefreshAsync(refreshed.RefreshToken, Guid.NewGuid(), default)));
    }

    [SqlAuthFact]
    public async Task Lifetime_reload_changes_future_issuance_without_extending_existing_sessions()
    {
        var (email, original) = await fixture.CreateSessionAsync();
        try
        {
            fixture.Configuration["Auth:Lifetimes:AccessTokenMinutes"] = "20";
            fixture.Configuration["Auth:Lifetimes:SessionDays"] = "2";
            fixture.Configuration.Reload();
            var before = DateTimeOffset.UtcNow;
            var refreshed = await fixture.UseAsync(auth => auth.RefreshAsync(original.RefreshToken, Guid.NewGuid(), default));
            Assert.Equal(original.Profile.SessionExpiresAt, refreshed.Profile.SessionExpiresAt);
            Assert.InRange((refreshed.Profile.AccessExpiresAt - before).TotalMinutes, 19.9, 20.1);
            var newSession = await fixture.UseAsync(auth => auth.LoginAsync(new LoginRequest(email,
                SqlAuthFixture.Password), default));
            Assert.InRange((newSession.Profile.SessionExpiresAt - before).TotalDays, 1.99, 2.01);
        }
        finally
        {
            fixture.Configuration["Auth:Lifetimes:AccessTokenMinutes"] = null;
            fixture.Configuration["Auth:Lifetimes:SessionDays"] = null;
            fixture.Configuration.Reload();
        }
    }

    [SqlAuthFact]
    public async Task Current_membership_and_administrator_status_are_checked_on_each_request()
    {
        var (_, pair) = await fixture.CreateSessionAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Users.Where(x => x.Id == pair.Profile.User.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsPlatformAdmin, true));
        var principal = SqlAuthFixture.Principal(pair);
        await scope.ServiceProvider.GetRequiredService<AuthSessionValidator>().ValidateAsync(principal, default);
        Assert.Equal("true", principal.FindFirstValue("platform_admin"));
        await db.Users.Where(x => x.Id == pair.Profile.User.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsPlatformAdmin, false));
        await scope.ServiceProvider.GetRequiredService<AuthSessionValidator>().ValidateAsync(principal, default);
        Assert.Null(principal.FindFirstValue("platform_admin"));
        await db.Memberships.Where(x => x.AccountId == pair.Profile.User.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        await Assert.ThrowsAsync<AuthProblem>(() => scope.ServiceProvider.GetRequiredService<AuthSessionValidator>()
            .ValidateAsync(principal, default));
    }

    [SqlAuthFact]
    public async Task Registration_requires_confirmation_and_failed_passwords_lock_out_the_account()
    {
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        await fixture.UseAsync(auth => auth.RegisterAsync(new RegisterRequest("未驗證", email, SqlAuthFixture.Password), default));
        var problem = await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.LoginAsync(new LoginRequest(email, SqlAuthFixture.Password), default)));
        Assert.Equal("email_unconfirmed", problem.Code);
        var link = fixture.Email.Link(email);
        await fixture.UseAsync(auth => auth.ConfirmEmailAsync(new ConfirmEmailRequest(link.UserId, link.Token), default));
        for (var attempt = 0; attempt < 5; attempt++)
            await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth => auth.LoginAsync(new LoginRequest(email,
                "incorrect password"), default)));
        var locked = await Assert.ThrowsAsync<AuthProblem>(() => fixture.UseAsync(auth =>
            auth.LoginAsync(new LoginRequest(email, SqlAuthFixture.Password), default)));
        Assert.Equal("invalid_credentials", locked.Code);
    }

    private async Task TryRefreshAsync(string token, Guid requestId)
    {
        try
        {
            await fixture.UseAsync(auth => auth.RefreshAsync(token, requestId, default));
        }
        catch (AuthProblem problem) when (problem.Code == "session_invalid")
        {
            // A concurrent revocation winning the account lock is an expected outcome.
        }
    }
}