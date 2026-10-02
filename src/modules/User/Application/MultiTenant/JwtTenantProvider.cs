using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using User.Infrastructure.MultiTenant;

namespace User.Application.MultiTenant;

/// <summary>
/// Extracts the tenant ID from the authenticated user's JWT token.
/// The JWT must contain a claim named "tenant_id".
/// </summary>
public class JwtTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public JwtTenantProvider(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public string? GetTenantId() => _httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id");
}