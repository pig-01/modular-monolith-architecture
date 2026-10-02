using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using User.Application.Commands;
using User.Application.Pipeline;
using User.Application.Validation;
using User.Infrastructure;

namespace User.IntegrationTest;

public class CreateUserCommandHandlerTests
{
    [Fact]
    public async Task Creates_user_and_returns_dto()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<UserDbContext>(options => options.UseInMemoryDatabase("user-int-tests"));
        services.AddMediator((MediatorOptions options) => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddValidatorsFromAssembly(typeof(CreateUserCommand).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new CreateUserCommand("Jane Doe", "jane@example.com"));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Jane Doe", result.Name);
    }
}
