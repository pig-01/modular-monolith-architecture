using DataSource.Application.Pipeline;
using DataSource.Application.Services;
using DataSource.Infrastructure;
using DataSource.Infrastructure.MultiDb;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataSource.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDataSourceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DataSourceDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IMultiDbContextFactory, MultiDbContextFactory>();
        services.AddScoped<IMultiSourceQueryService, MultiSourceQueryService>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}