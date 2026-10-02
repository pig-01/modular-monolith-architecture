using Order.Application.Abstractions;
using Riok.Mapperly.Abstractions;
using OrderEntity = Order.Domain.Entities.Order;

namespace Order.Application.Mapping;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class OrderMapper
{
    public static partial OrderDto ToDto(OrderEntity source);

    public static partial IQueryable<Queries.OrderDto> ProjectToDto(this IQueryable<OrderEntity> source);
}