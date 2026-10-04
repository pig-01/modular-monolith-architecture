using Auth.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AuthAccount, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AuthSession> Sessions => Set<AuthSession>();
    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();
    public DbSet<RefreshTokenRecord> RefreshTokens => Set<RefreshTokenRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AuthAccount>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.HasIndex(x => x.NormalizedEmail).IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        });
        builder.Entity<AuthSession>(entity =>
        {
            entity.ToTable("AuthSessions");
            entity.Property(x => x.TenantId).HasMaxLength(100);
            entity.Property(x => x.SecurityStamp).HasMaxLength(256);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.AccountId);
            entity.HasOne<AuthAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<TenantMembership>(entity =>
        {
            entity.ToTable("AuthMemberships");
            entity.HasKey(x => new { x.AccountId, x.TenantId });
            entity.Property(x => x.TenantId).HasMaxLength(100);
            entity.HasOne<AuthAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<RefreshTokenRecord>(entity =>
        {
            entity.ToTable("AuthRefreshTokens");
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength();
            entity.Property(x => x.SuccessorHash).HasMaxLength(64).IsFixedLength();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.SessionId, x.RequestId });
            entity.HasIndex(x => x.ReplayUntil);
            entity.HasOne<AuthSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}