using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Product.Application.Commands;
using Product.Application.Pipeline;
using Product.Application.Validation;
using Product.Infrastructure;

namespace Product.IntegrationTest;

public class CreateProductCommandHandlerTests
{
    [Fact]
    public async Task Creates_product_and_returns_dto()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<ProductDbContext>(options => options.UseInMemoryDatabase("product-int-tests"));
        services.AddMediator((MediatorOptions options) => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddValidatorsFromAssembly(typeof(CreateProductCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new CreateProductCommand("Gadget", 10m));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(10m, result.Price);
    }
}