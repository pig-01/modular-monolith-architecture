using System.Security.Claims;
using Mediator;

namespace Auth.Application;

public sealed record RegisterCommand(RegisterRequest Request) : ICommand<Unit>;
public sealed record ConfirmEmailCommand(ConfirmEmailRequest Request) : ICommand<Unit>;
public sealed record ResendConfirmationCommand(EmailRequest Request) : ICommand<Unit>;
public sealed record ForgotPasswordCommand(EmailRequest Request) : ICommand<Unit>;
public sealed record ResetPasswordCommand(ResetPasswordRequest Request) : ICommand<Unit>;
public sealed record LoginCommand(LoginRequest Request) : ICommand<SessionTokens>;
public sealed record RefreshCommand(string? Token, Guid RequestId) : ICommand<SessionTokens>;
public sealed record SwitchTenantCommand(ClaimsPrincipal Principal, SwitchTenantRequest Request) : ICommand<SessionTokens>;
public sealed record LogoutCommand(ClaimsPrincipal Principal, string? Token) : ICommand<Unit>;
public sealed record SessionProfileQuery(ClaimsPrincipal Principal) : IQuery<SessionProfile>;

public sealed class RegisterCommandHandler(AuthService auth) : ICommandHandler<RegisterCommand, Unit>
{
    public async ValueTask<Unit> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        await auth.RegisterAsync(command.Request, cancellationToken);
        return Unit.Value;
    }
}

public sealed class ConfirmEmailCommandHandler(AuthService auth) : ICommandHandler<ConfirmEmailCommand, Unit>
{
    public async ValueTask<Unit> Handle(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        await auth.ConfirmEmailAsync(command.Request, cancellationToken);
        return Unit.Value;
    }
}

public sealed class ResendConfirmationCommandHandler(AuthService auth) : ICommandHandler<ResendConfirmationCommand, Unit>
{
    public async ValueTask<Unit> Handle(ResendConfirmationCommand command, CancellationToken cancellationToken)
    {
        await auth.ResendConfirmationAsync(command.Request, cancellationToken);
        return Unit.Value;
    }
}

public sealed class ForgotPasswordCommandHandler(AuthService auth) : ICommandHandler<ForgotPasswordCommand, Unit>
{
    public async ValueTask<Unit> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        await auth.ForgotPasswordAsync(command.Request, cancellationToken);
        return Unit.Value;
    }
}

public sealed class ResetPasswordCommandHandler(AuthService auth) : ICommandHandler<ResetPasswordCommand, Unit>
{
    public async ValueTask<Unit> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        await auth.ResetPasswordAsync(command.Request, cancellationToken);
        return Unit.Value;
    }
}

public sealed class LoginCommandHandler(AuthService auth) : ICommandHandler<LoginCommand, SessionTokens>
{
    public ValueTask<SessionTokens> Handle(LoginCommand command, CancellationToken cancellationToken) =>
        new(auth.LoginAsync(command.Request, cancellationToken));
}

public sealed class RefreshCommandHandler(AuthService auth) : ICommandHandler<RefreshCommand, SessionTokens>
{
    public ValueTask<SessionTokens> Handle(RefreshCommand command, CancellationToken cancellationToken) =>
        new(auth.RefreshAsync(command.Token, command.RequestId, cancellationToken));
}

public sealed class SwitchTenantCommandHandler(AuthService auth) : ICommandHandler<SwitchTenantCommand, SessionTokens>
{
    public ValueTask<SessionTokens> Handle(SwitchTenantCommand command, CancellationToken cancellationToken) =>
        new(auth.SwitchTenantAsync(command.Principal, command.Request, cancellationToken));
}

public sealed class LogoutCommandHandler(AuthService auth) : ICommandHandler<LogoutCommand, Unit>
{
    public async ValueTask<Unit> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(command.Principal, command.Token, cancellationToken);
        return Unit.Value;
    }
}

public sealed class SessionProfileQueryHandler(AuthService auth) : IQueryHandler<SessionProfileQuery, SessionProfile>
{
    public ValueTask<SessionProfile> Handle(SessionProfileQuery query, CancellationToken cancellationToken) =>
        new(auth.GetProfileAsync(query.Principal, cancellationToken));
}