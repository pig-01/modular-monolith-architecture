using Mediator;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Auth.Application;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth").WithTags("Authentication")
            .AddEndpointFilter<AuthAntiforgeryFilter>()
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { tokens.RequestToken });
        }).AllowAnonymous();

        group.MapPost("/register", async (RegisterRequest request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            await mediator.Send(new RegisterCommand(request), cancellationToken);
            return Results.Accepted();
        }).AllowAnonymous().RequireRateLimiting("auth-email");

        group.MapPost("/confirm-email", async (ConfirmEmailRequest request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            await mediator.Send(new ConfirmEmailCommand(request), cancellationToken);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("auth");

        group.MapPost("/resend-confirmation", async (EmailRequest request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            await mediator.Send(new ResendConfirmationCommand(request), cancellationToken);
            return Results.Accepted();
        }).AllowAnonymous().RequireRateLimiting("auth-email");

        group.MapPost("/forgot-password", async (EmailRequest request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            await mediator.Send(new ForgotPasswordCommand(request), cancellationToken);
            return Results.Accepted();
        }).AllowAnonymous().RequireRateLimiting("auth-email");

        group.MapPost("/reset-password", async (ResetPasswordRequest request, IMediator mediator, CancellationToken cancellationToken) =>
        {
            await mediator.Send(new ResetPasswordCommand(request), cancellationToken);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("auth");

        group.MapPost("/login", async (LoginRequest request, HttpContext context, IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var tokens = await mediator.Send(new LoginCommand(request), cancellationToken);
            AuthCookies.Set(context.Response, tokens);
            return Results.Ok(tokens.Profile);
        }).AllowAnonymous().RequireRateLimiting("auth");

        group.MapPost("/refresh", async (RefreshRequest request, HttpContext context, IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var tokens = await mediator.Send(new RefreshCommand(context.Request.Cookies[AuthCookies.Refresh],
                request.RequestId), cancellationToken);
            AuthCookies.Set(context.Response, tokens);
            return Results.Ok(tokens.Profile);
        }).AllowAnonymous().RequireRateLimiting("auth");

        group.MapPost("/logout", async (HttpContext context, IMediator mediator, CancellationToken cancellationToken) =>
        {
            await mediator.Send(new LogoutCommand(context.User, context.Request.Cookies[AuthCookies.Refresh]), cancellationToken);
            AuthCookies.Clear(context.Response);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("auth");

        group.MapGet("/me", async (HttpContext context, IMediator mediator, CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new SessionProfileQuery(context.User), cancellationToken))).RequireAuthorization();

        group.MapPost("/switch-tenant", async (SwitchTenantRequest request, HttpContext context, IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var tokens = await mediator.Send(new SwitchTenantCommand(context.User, request), cancellationToken);
            AuthCookies.Set(context.Response, tokens);
            return Results.Ok(tokens.Profile);
        }).RequireAuthorization().RequireRateLimiting("auth");
        return endpoints;
    }
}