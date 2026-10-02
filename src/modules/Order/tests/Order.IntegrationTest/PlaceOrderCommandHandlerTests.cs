using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Order.Application.Commands;
using Order.Application.Pipeline;
using Order.Application.Validation;
using Order.Infrastructure;

namespace Order.IntegrationTest;

public class PlaceOrderCommandHandlerTests
{
    [Fact]
    public async Task Places_order_and_returns_dto()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<OrderDbContext>(options => options.UseInMemoryDatabase("order-int-tests"));
        services.AddMediator((MediatorOptions options) => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddValidatorsFromAssembly(typeof(PlaceOrderCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new PlaceOrderCommand(Guid.NewGuid(), new[]
        {
            new PlaceOrderItem(Guid.NewGuid(), 1, 9.99m)
        }));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Single(result.Items);
        Assert.Equal(9.99m, result.Total);
    }
}