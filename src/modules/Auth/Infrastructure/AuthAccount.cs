using Microsoft.AspNetCore.Identity;

namespace Auth.Infrastructure;

public sealed class AuthAccount : IdentityUser<Guid>
{
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsPlatformAdmin { get; set; }
}