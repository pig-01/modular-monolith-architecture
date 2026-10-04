using Auth.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Application;

public sealed class AuthMaintenanceService(IServiceScopeFactory scopes, AuthLifetimePolicy lifetimes,
    TimeProvider time, ILogger<AuthMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = lifetimes.Current; // Fail startup early for invalid initial configuration.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                var now = time.GetUtcNow();
                await db.RefreshTokens.Where(x => x.ReplayUntil <= now && x.ProtectedReplay != null)
                    .ExecuteUpdateAsync(set => set.SetProperty(x => x.ProtectedReplay, (string?)null), stoppingToken);
                var retention = now.AddDays(-30);
                await db.Sessions.Where(x => x.ExpiresAt < retention).ExecuteDeleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError("Auth maintenance failed ({FailureType}); retrying at the next interval.", exception.GetType().Name);
            }
        }
    }
}