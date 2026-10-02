using Mediator;
using Microsoft.EntityFrameworkCore;
using User.Application.Abstractions;
using User.Application.Commands;
using User.Application.Mapping;
using User.Domain.Entities;
using User.Domain.Events;
using User.Infrastructure;
using User.IntegrationEvent.Events;

namespace User.Application.Commands;

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly UserDbContext _dbContext;
    private readonly IMediator _mediator;

    public CreateUserCommandHandler(UserDbContext dbContext, IMediator mediator)
    {
        _dbContext = dbContext;
        _mediator = mediator;
    }

    public async ValueTask<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        Domain.Entities.User entity = new(Guid.NewGuid(), request.Name, request.Email);
        await _dbContext.Users.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await PublishDomainEvents(entity, cancellationToken);
        await _mediator.Publish(new UserCreatedIntegrationEvent(entity.Id, entity.Name, entity.Email), cancellationToken);
        entity.ClearDomainEvents();

        return UserMapper.ToDto(entity);
    }

    private async Task PublishDomainEvents(User.Domain.Entities.User entity, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in entity.DomainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }
}