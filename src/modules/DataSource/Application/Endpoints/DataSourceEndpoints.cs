using DataSource.Application.Commands;
using DataSource.Application.Queries;
using DataSource.Application.Services;
using DataSource.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace DataSource.Application.Endpoints;

public static class DataSourceEndpoints
{
    public static IEndpointRouteBuilder MapDataSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/datasources").RequireAuthorization();

        // Register a new data source for the authenticated user
        group.MapPost("/", async (RegisterDataSourceRequest body, HttpContext ctx, IMediator mediator) =>
        {
            var userId = GetUserId(ctx);
            var command = new RegisterDataSourceCommand(userId, body.Name, body.Provider, body.ConnectionString);
            var result = await mediator.Send(command);
            return Results.Created($"/datasources/{result.Id}", result);
        })
        .WithName("RegisterDataSource");

        // List all data sources belonging to the authenticated user
        group.MapGet("/", async (HttpContext ctx, IMediator mediator) =>
        {
            var userId = GetUserId(ctx);
            var result = await mediator.Send(new GetUserDataSourcesQuery(userId));
            return Results.Ok(result);
        })
        .WithName("GetUserDataSources");

        // Remove a data source (ownership is validated in the handler)
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext ctx, IMediator mediator) =>
        {
            var userId = GetUserId(ctx);
            var deleted = await mediator.Send(new DeleteDataSourceCommand(id, userId));
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteDataSource");

        // Query Users table across ALL registered data sources in parallel
        group.MapGet("/query/users", async (HttpContext ctx, IMultiSourceQueryService queryService) =>
        {
            var userId = GetUserId(ctx);
            var results = await queryService.QueryUsersAsync(userId, ctx.RequestAborted);
            return Results.Ok(results);
        })
        .WithName("QueryUsersAcrossAllSources");

        return endpoints;
    }

    private static Guid GetUserId(HttpContext ctx)
    {
        var raw = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? ctx.User.FindFirstValue("sub")
               ?? ctx.User.FindFirstValue("user_id");

        if (Guid.TryParse(raw, out var userId))
            return userId;

        throw new UnauthorizedAccessException("A valid 'sub' or 'user_id' claim is required in the JWT token.");
    }
}

// Request body DTO — connection string comes from the client (store encrypted in production)
public record RegisterDataSourceRequest(string Name, ProviderType Provider, string ConnectionString);
