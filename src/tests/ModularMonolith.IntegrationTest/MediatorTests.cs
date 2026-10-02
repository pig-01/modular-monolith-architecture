using DataSource.Application;
using DataSource.Application.Commands;
using DataSource.Application.Queries;
using DataSource.Domain.Enums;
using DataSource.Infrastructure;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ModularMonolith.WebApi;
using Order.Application;
using Order.Application.Commands;
using Order.Application.Queries;
using Order.Infrastructure;
using Product.Application;
using Product.Application.Commands;
using Product.Application.Queries;
using Product.Infrastructure;
using User.Application;
using User.Application.Commands;
using User.Application.Queries;
using User.Infrastructure;

namespace ModularMonolith.IntegrationTest;

public class MediatorTests
{
    [Fact]
    public async Task Host_dispatches_commands_queries_and_domain_events_for_all_modules()
    {
        var log = new EventLog();
        using var provider = BuildProvider(log);
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var mediator = services.GetRequiredService<IMediator>();

        var user = await mediator.Send(new CreateUserCommand("Jane", "jane@example.com"));
        var product = await mediator.Send(new CreateProductCommand("Gadget", 10m));
        var order = await mediator.Send(new PlaceOrderCommand(user.Id,
            [new PlaceOrderItem(product.Id, 2, product.Price)]));
        var source = await mediator.Send(new RegisterDataSourceCommand(user.Id, "Reporting",
            ProviderType.MSSQL, "Server=unused;Database=reporting"));

        Assert.Equal(user.Id, Assert.Single(await mediator.Send(new GetUsersQuery())).Id);
        Assert.Equal(user.Email, (await mediator.Send(new GetUserByIdQuery(user.Id)))!.Email);
        Assert.Equal(product.Id, Assert.Single(await mediator.Send(new GetProductsQuery())).Id);
        Assert.Equal(product.Price, (await mediator.Send(new GetProductByIdQuery(product.Id)))!.Price);
        Assert.Equal(order.Id, Assert.Single(await mediator.Send(new GetOrdersQuery())).Id);
        Assert.Equal(20m, (await mediator.Send(new GetOrderByIdQuery(order.Id)))!.Total);
        Assert.Equal(source.Id, Assert.Single(await mediator.Send(new GetUserDataSourcesQuery(user.Id))).Id);

        Assert.Null(await mediator.Send(new GetUserByIdQuery(Guid.NewGuid())));
        Assert.Null(await mediator.Send(new GetProductByIdQuery(Guid.NewGuid())));
        Assert.Null(await mediator.Send(new GetOrderByIdQuery(Guid.NewGuid())));
        Assert.Empty(await mediator.Send(new GetUserDataSourcesQuery(Guid.NewGuid())));
        Assert.False(await mediator.Send(new DeleteDataSourceCommand(source.Id, Guid.NewGuid())));
        Assert.True(await mediator.Send(new DeleteDataSourceCommand(source.Id, user.Id)));
        Assert.False(await mediator.Send(new DeleteDataSourceCommand(source.Id, user.Id)));
        Assert.Empty(await mediator.Send(new GetUserDataSourcesQuery(user.Id)));

        Assert.Single(log.Messages, message => message.StartsWith("User registered:"));
        Assert.Single(log.Messages, message => message.StartsWith("Product created:"));
        Assert.Single(log.Messages, message => message.StartsWith("Order placed:"));
        Assert.Empty((await services.GetRequiredService<UserDbContext>().Users.SingleAsync()).DomainEvents);
        Assert.Empty((await services.GetRequiredService<ProductDbContext>().Products.SingleAsync()).DomainEvents);
        Assert.Empty((await services.GetRequiredService<OrderDbContext>().Orders.SingleAsync()).DomainEvents);
    }

    [Fact]
    public async Task Validation_rejects_invalid_commands_before_persistence()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var mediator = services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ValidationException>(() =>
            mediator.Send(new CreateUserCommand("", "invalid")).AsTask());
        await Assert.ThrowsAsync<ValidationException>(() =>
            mediator.Send(new CreateProductCommand("", 0m)).AsTask());
        await Assert.ThrowsAsync<ValidationException>(() =>
            mediator.Send(new PlaceOrderCommand(Guid.Empty, [])).AsTask());
        await Assert.ThrowsAsync<ValidationException>(() =>
            mediator.Send(new RegisterDataSourceCommand(Guid.Empty, "", ProviderType.MSSQL, "")).AsTask());

        Assert.Empty(await services.GetRequiredService<UserDbContext>().Users.ToListAsync());
        Assert.Empty(await services.GetRequiredService<ProductDbContext>().Products.ToListAsync());
        Assert.Empty(await services.GetRequiredService<OrderDbContext>().Orders.ToListAsync());
        Assert.Empty(await services.GetRequiredService<DataSourceDbContext>().DataSources.ToListAsync());
    }

    [Fact]
    public async Task Cancellation_reaches_query_handlers_through_pipelines()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            mediator.Send(new GetUsersQuery(), cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            mediator.Send(new GetProductsQuery(), cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            mediator.Send(new GetOrdersQuery(), cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            mediator.Send(new GetUserDataSourcesQuery(Guid.NewGuid()), cancellation.Token).AsTask());
    }

    [Fact]
    public async Task Mediator_and_handlers_use_the_current_scope()
    {
        using var provider = BuildProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var firstMediator = first.ServiceProvider.GetRequiredService<IMediator>();
        var secondMediator = second.ServiceProvider.GetRequiredService<IMediator>();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IMediator>());
        Assert.Same(firstMediator, first.ServiceProvider.GetRequiredService<IMediator>());
        Assert.NotSame(firstMediator, secondMediator);
        Assert.NotSame(first.ServiceProvider.GetRequiredService<CreateUserCommandHandler>(),
            second.ServiceProvider.GetRequiredService<CreateUserCommandHandler>());

        var firstUser = await firstMediator.Send(new CreateUserCommand("First", "first@example.com"));
        var secondUser = await second.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateUserCommand("Second", "second@example.com"));
        var firstContext = first.ServiceProvider.GetRequiredService<UserDbContext>();
        var secondContext = second.ServiceProvider.GetRequiredService<UserDbContext>();
        Assert.Equal(firstUser.Id, Assert.Single(firstContext.Users.Local).Id);
        Assert.Equal(secondUser.Id, Assert.Single(secondContext.Users.Local).Id);
    }

    private static ServiceProvider BuildProvider(EventLog? log = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=unused;Database=unused"
            }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddProvider(log ?? new EventLog()));
        services.AddUserModule(configuration);
        services.AddProductModule(configuration);
        services.AddOrderModule(configuration);
        services.AddDataSourceModule(configuration);
        services.AddApplicationMediator();

        // Replace only database services: exercise the production mediator and module pipelines.
        UseInMemory<UserDbContext>(services, options => new UserDbContext(options));
        UseInMemory<ProductDbContext>(services, options => new ProductDbContext(options));
        UseInMemory<OrderDbContext>(services, options => new OrderDbContext(options));
        UseInMemory<DataSourceDbContext>(services, options => new DataSourceDbContext(options));
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    private static void UseInMemory<TContext>(IServiceCollection services,
        Func<DbContextOptions<TContext>, TContext> factory) where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        services.RemoveAll<TContext>();
        services.AddScoped(_ => factory(options));
    }

    private sealed class EventLog : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new EventLogger(Messages);
        public void Dispose() { }

        private sealed class EventLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Add(formatter(state, exception));
        }
    }
}
