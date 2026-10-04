using Auth.Application;
using Auth.Infrastructure;
using DataSource.Application;
using DataSource.Application.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using ModularMonolith.WebApi;
using Order.Application;
using Order.Application.Endpoints;
using Product.Application;
using Product.Application.Endpoints;
using Serilog;
using User.Application;
using User.Application.Endpoints;

var builder = WebApplication.CreateBuilder(args);
var settingsFile = builder.Configuration["Auth:SettingsFile"];
if (!string.IsNullOrWhiteSpace(settingsFile))
{
    builder.Configuration.AddAuthSettingsFile(settingsFile, failureType =>
        Log.Warning("Auth settings reload rejected ({FailureType}); retaining the previous settings.", failureType));
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}

builder.Host.UseSerilog((context, loggerConfiguration) =>
    loggerConfiguration.ReadFrom.Configuration(context.Configuration));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        Description = "A current server-issued access JWT. Do not combine Bearer with an authentication cookie."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

builder.Services.AddWebSecurity(builder.Configuration);
builder.Services.AddAuthModule(builder.Configuration, builder.Environment);
builder.Services.AddUserModule(builder.Configuration);
builder.Services.AddOrderModule(builder.Configuration);
builder.Services.AddProductModule(builder.Configuration);
builder.Services.AddDataSourceModule(builder.Configuration);
builder.Services.AddApplicationMediator();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseSecurityHeaders();
app.UseMiddleware<AuthExceptionMiddleware>();
app.UseSerilogRequestLogging();
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("Browser");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapAuthEndpoints();
var business = app.MapGroup("").RequireAuthorization("PlatformAdmin")
    .AddEndpointFilter<CookieAntiforgeryFilter>();
business.MapUserEndpoints();
business.MapOrderEndpoints();
business.MapProductEndpoints();
business.MapDataSourceEndpoints();
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (AuthDbContext database, CancellationToken cancellationToken) =>
{
    var ready = await database.Database.CanConnectAsync(cancellationToken);
    return Results.Json(new { status = ready ? "healthy" : "unhealthy" }, statusCode: ready ? 200 : 503);
}).AllowAnonymous();
app.Run();

public partial class Program;