using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Product.Application.Commands;
using Product.Application.Queries;

namespace Product.Application.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/products");

        group.MapPost("/", async (CreateProductCommand command, IMediator mediator) =>
        {
            var result = await mediator.Send(command);
            return Results.Created($"/products/{result.Id}", result);
        })
        .WithName("CreateProduct");

        group.MapGet("/", async (IMediator mediator) =>
        {
            var result = await mediator.Send(new GetProductsQuery());
            return Results.Ok(result);
        })
        .WithName("GetProducts");

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var result = await mediator.Send(new GetProductByIdQuery(id));
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetProductById");

        return endpoints;
    }
}