using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Order.Application.Commands;
using Order.Application.Queries;

namespace Order.Application.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/orders");

        group.MapPost("/", async (PlaceOrderCommand command, IMediator mediator) =>
        {
            var result = await mediator.Send(command);
            return Results.Created($"/orders/{result.Id}", result);
        })
        .WithName("PlaceOrder");

        group.MapGet("/", async (IMediator mediator) =>
        {
            var result = await mediator.Send(new GetOrdersQuery());
            return Results.Ok(result);
        })
        .WithName("GetOrders");

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetOrderByIdQuery(id));
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetOrderById");

        return endpoints;
    }
}
