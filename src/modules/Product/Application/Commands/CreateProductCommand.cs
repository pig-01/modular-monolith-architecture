using Mediator;
using Product.Application.Abstractions;

namespace Product.Application.Commands;

public record CreateProductCommand(string Name, decimal Price) : IRequest<ProductDto>;
