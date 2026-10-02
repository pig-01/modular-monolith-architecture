namespace User.Infrastructure.MultiTenant;

public interface ITenantProvider
{
    string? GetTenantId();
}