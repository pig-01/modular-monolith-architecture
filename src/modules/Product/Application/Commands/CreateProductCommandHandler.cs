using Mediator;
using Product.Application.Abstractions;
using Product.Domain.Entities;
using Product.Domain.Events;
using Product.Infrastructure;
using Product.IntegrationEvent.Events;

namespace Product.Application.Commands;

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, ProductDto>
{
    private readonly ProductDbContext _dbContext;
    private readonly IMediator _mediator;

    public CreateProductCommandHandler(ProductDbContext dbContext, IMediator mediator)
    {
        _dbContext = dbContext;
        _mediator = mediator;
    }

    public async ValueTask<ProductDto> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var entity = new Product.Domain.Entities.Product(Guid.NewGuid(), request.Name, request.Price);
        await _dbContext.Products.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await PublishDomainEvents(entity, cancellationToken);
        await _mediator.Publish(new ProductCreatedIntegrationEvent(entity.Id, entity.Name, entity.Price), cancellationToken);
        entity.ClearDomainEvents();

        return new ProductDto(entity.Id, entity.Name, entity.Price);
    }

    private async Task PublishDomainEvents(Product.Domain.Entities.Product entity, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in entity.DomainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }
}
