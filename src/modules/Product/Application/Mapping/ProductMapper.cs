using Product.Application.Abstractions;
using Riok.Mapperly.Abstractions;
using ProductEntity = Product.Domain.Entities.Product;

namespace Product.Application.Mapping;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class ProductMapper
{
    public static partial ProductDto ToDto(ProductEntity source);

    public static partial IQueryable<Queries.ProductDto> ProjectToDto(this IQueryable<ProductEntity> source);
}