using Mediator;

namespace ModularMonolith.WebApi;

public static class MediatorRegistration
{
    public static IServiceCollection AddApplicationMediator(this IServiceCollection services) =>
        // Generate one mediator for all referenced modules. Handlers share the request's DbContext scope.
        services.AddMediator((MediatorOptions options) => options.ServiceLifetime = ServiceLifetime.Scoped);
}