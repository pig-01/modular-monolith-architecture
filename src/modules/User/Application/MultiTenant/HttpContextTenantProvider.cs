using Microsoft.AspNetCore.Http;
using User.Infrastructure.MultiTenant;

namespace User.Application.MultiTenant;

/// <summary>
/// Resolves the current tenant identifier from the active HTTP request.
///
/// Resolution order:
///   1. JWT claim  "tenant_id"
///   2. Request header "X-Tenant-Id"
///   3. <see cref="TenantConstants.Default"/> as fallback
/// </summary>
public class HttpContextTenantProvider : ITenantProvider
{
    private const string TenantIdClaimType = "tenant_id";
    private const string TenantIdHeaderName = "X-Tenant-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextTenantProvider(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public string GetTenantId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return TenantConstants.Default;

        var tenantClaim = context.User.FindFirst(TenantIdClaimType)?.Value;
        if (!string.IsNullOrWhiteSpace(tenantClaim))
            return tenantClaim;

        if (context.Request.Headers.TryGetValue(TenantIdHeaderName, out var headerValue)
            && !string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.ToString();
        }

        return TenantConstants.Default;
    }
}