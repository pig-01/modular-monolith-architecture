using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Auth.Infrastructure;

/// <summary>All security writes for an account share a SQL transaction-owned lock, including password reset.</summary>
public static class AuthAccountLock
{
    public static async Task<IDbContextTransaction> AcquireAsync(AuthDbContext db, Guid accountId, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var resource = $"Auth.Account.{accountId:D}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction', @LockTimeout = 10000;
                IF @result < 0 THROW 51000, 'Auth account lock unavailable.', 1;
                """, cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}