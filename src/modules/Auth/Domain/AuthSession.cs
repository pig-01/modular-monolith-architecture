namespace Auth.Domain;

public sealed class AuthSession
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string TenantId { get; set; } = "";
    public string SecurityStamp { get; set; } = "";
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TenantMembership
{
    public Guid AccountId { get; set; }
    public string TenantId { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class RefreshTokenRecord
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string TokenHash { get; set; } = "";
    public long SessionVersion { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid? RequestId { get; set; }
    public string? SuccessorHash { get; set; }
    public DateTimeOffset? ReplayUntil { get; set; }
    public string? ProtectedReplay { get; set; }
}