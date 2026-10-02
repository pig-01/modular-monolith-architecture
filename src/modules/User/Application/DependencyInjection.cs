using FluentValidation;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using User.Application.MultiTenant;
using User.Application.Pipeline;
using User.Infrastructure;
using User.Infrastructure.MultiTenant;

namespace User.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddUserModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<ITenantConnectionStringResolver, InMemoryTenantConnectionStringResolver>();
        services.AddScoped<ITenantProvider, JwtTenantProvider>();
        services.AddScoped<TenantUserDbContextFactory>();

        // Register UserDbContext as scoped, delegating creation to the tenant factory.
        // This allows all existing handlers to inject UserDbContext without any changes —
        // they automatically get the correct tenant's database connection.
        services.AddScoped<UserDbContext>(sp =>
        {
            var factory = sp.GetRequiredService<TenantUserDbContextFactory>();
            return factory.CreateDbContext();
        });

        // Register UserDbContext by delegating to the factory so the connection string
        // resolution logic lives in exactly one place (TenantUserDbContextFactory).
        services.AddScoped<UserDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<ITenantDbContextFactory<UserDbContext>>()
                           .CreateDbContext());

        // --- CQRS pipeline ---
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}