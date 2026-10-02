using Mediator;
using Microsoft.EntityFrameworkCore;
using User.Application.Mapping;
using User.Infrastructure;

namespace User.Application.Queries;

public record UserDto(Guid Id, string Name, string Email);
public record GetUsersQuery : IRequest<IReadOnlyList<UserDto>>;
public record GetUserByIdQuery(Guid Id) : IRequest<UserDto?>;

public sealed class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, IReadOnlyList<UserDto>>
{
    private readonly UserDbContext _dbContext;

    public GetUsersQueryHandler(UserDbContext dbContext) => _dbContext = dbContext;

    public async ValueTask<IReadOnlyList<UserDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken) => await _dbContext.Users
            .AsNoTracking()
            .ProjectToDto()
            .ToListAsync(cancellationToken);
}

public sealed class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    private readonly UserDbContext _dbContext;

    public GetUserByIdQueryHandler(UserDbContext dbContext) => _dbContext = dbContext;

    public async ValueTask<UserDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken) => await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == request.Id)
            .ProjectToDto()
            .FirstOrDefaultAsync(cancellationToken);
}