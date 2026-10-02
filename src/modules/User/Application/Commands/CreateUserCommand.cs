using Mediator;
using User.Application.Abstractions;

namespace User.Application.Commands;

public record CreateUserCommand(string Name, string Email) : IRequest<UserDto>;