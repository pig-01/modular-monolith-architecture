using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace ModularMonolith.WebApi;

public static class WebSecurity
{
    public static IServiceCollection AddWebSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || uri.GetLeftPart(UriPartial.Authority) != origin || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("CORS origins must be explicit HTTPS origins without paths or credentials.");
        }

        services.AddCors(cors => cors.AddPolicy("Browser", policy =>
        {
            if (origins.Length == 0)
                return;
            policy.WithOrigins(origins).AllowCredentials()
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                .WithHeaders("Content-Type", "X-CSRF-TOKEN", "Authorization")
                .WithExposedHeaders("Retry-After");
        }));
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var address in configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
                options.KnownProxies.Add(IPAddress.Parse(address));
            foreach (var network in configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });
        services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(180));
        return services;
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                if (context.Request.Path.StartsWithSegments("/auth"))
                    context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
            await next(context);
        });
}