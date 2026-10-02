using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using User.Application.Commands;
using User.Application.Queries;
using User.Infrastructure;

namespace User.Application.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/users");

        group.MapPost("/", async (CreateUserCommand command, IMediator mediator) =>
        {
            var result = await mediator.Send(command);
            return Results.Created($"/users/{result.Id}", result);
        })
        .WithName("CreateUser");

        group.MapGet("/", async (IMediator mediator) =>
        {
            var result = await mediator.Send(new GetUsersQuery());
            return Results.Ok(result);
        })
        .WithName("GetUsers");

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetUserByIdQuery(id));
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetUserById");

        return endpoints;
    }
}
