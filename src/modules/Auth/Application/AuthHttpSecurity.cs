using System.Data.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Application;

public static class AuthCookies
{
    public const string Access = "__Host-auth-access";
    public const string Refresh = "__Host-auth-refresh";
    public const string Antiforgery = "__Host-auth-csrf";
    public const string CsrfHeader = "X-CSRF-TOKEN";

    public static void Set(HttpResponse response, SessionTokens tokens)
    {
        response.Cookies.Append(Access, tokens.AccessToken, Options(tokens.Profile.AccessExpiresAt));
        response.Cookies.Append(Refresh, tokens.RefreshToken, Options(tokens.Profile.SessionExpiresAt));
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Access, Options(null));
        response.Cookies.Delete(Refresh, Options(null));
    }

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
        IsEssential = true
    };
}

/// <summary>For unsafe business endpoints. Only a validated, cookie-free Bearer request may bypass CSRF.</summary>
public sealed class CookieAntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return await next(invocation);
        var bearerOnly = context.User.Identity?.IsAuthenticated == true
            && context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && !context.Request.Cookies.ContainsKey(AuthCookies.Access)
            && !context.Request.Cookies.ContainsKey(AuthCookies.Refresh);
        if (!bearerOnly)
            await ValidateAsync(antiforgery, context);
        return await next(invocation);
    }

    internal static async Task ValidateAsync(IAntiforgery antiforgery, HttpContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            throw new AuthProblem(400, "csrf_failed", "安全驗證已過期，請重試。");
        }
    }
}

public sealed class AuthAntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        if (!HttpMethods.IsGet(invocation.HttpContext.Request.Method))
            await CookieAntiforgeryFilter.ValidateAsync(antiforgery, invocation.HttpContext);
        return await next(invocation);
    }
}

public sealed class AuthExceptionMiddleware(RequestDelegate next, ILogger<AuthExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Conflict is rejected even on anonymous auth endpoints; never silently choose credentials.
            if (context.Request.Cookies.ContainsKey(AuthCookies.Access)
                && context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                throw new AuthProblem(400, "credential_conflict", "請僅使用一種登入憑證。");
            await next(context);
            // Minimal API JSON binding can set an empty 400 without throwing in production.
            if (context.Request.Path.StartsWithSegments("/auth") && context.Response.StatusCode == 400
                && !context.Response.HasStarted && string.IsNullOrEmpty(context.Response.ContentType)
                && context.Response.ContentLength is null or 0)
                await WriteProblemAsync(context, 400, "validation_failed", "請檢查輸入欄位。");
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == 400 && !context.Response.HasStarted)
        {
            await WriteProblemAsync(context, 400, "validation_failed", "請檢查輸入欄位。");
        }
        catch (AuthProblem problem) when (!context.Response.HasStarted)
        {
            await WriteProblemAsync(context, problem.Status, problem.Code, problem.Message, problem.Errors);
        }
        catch (Exception exception) when (!context.Response.HasStarted
            && exception is DbException or DbUpdateException or TimeoutException)
        {
            // Avoid logging connection strings, SQL parameter values, or credential-bearing exceptions.
            logger.LogError("Authentication storage unavailable ({FailureType}).", exception.GetType().Name);
            await WriteProblemAsync(context, 503, "service_unavailable", "服務暫時無法使用，請稍後重試。");
        }
    }

    public static Task WriteProblemAsync(HttpContext context, int status, string code, string title,
        IDictionary<string, string[]>? errors = null)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        return Results.Problem(statusCode: status, title: title, type: $"urn:auth:error:{code}",
            extensions: new Dictionary<string, object?> { ["code"] = code, ["errors"] = errors }).ExecuteAsync(context);
    }
}