using Mediator;
using Microsoft.EntityFrameworkCore;
using Product.Application.Mapping;
using Product.Infrastructure;

namespace Product.Application.Queries;

public record ProductDto(Guid Id, string Name, decimal Price);
public record GetProductsQuery : IRequest<IReadOnlyList<ProductDto>>;
public record GetProductByIdQuery(Guid Id) : IRequest<ProductDto?>;

public sealed class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, IReadOnlyList<ProductDto>>
{
    private readonly ProductDbContext _dbContext;

    public GetProductsQueryHandler(ProductDbContext dbContext) => _dbContext = dbContext;

    public async ValueTask<IReadOnlyList<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken) => await _dbContext.Products
            .AsNoTracking()
            .ProjectToDto()
            .ToListAsync(cancellationToken);
}

public sealed class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    private readonly ProductDbContext _dbContext;

    public GetProductByIdQueryHandler(ProductDbContext dbContext) => _dbContext = dbContext;

    public async ValueTask<ProductDto?> Handle(GetProductByIdQuery request, CancellationToken cancellationToken) => await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.Id == request.Id)
            .ProjectToDto()
            .FirstOrDefaultAsync(cancellationToken);
}