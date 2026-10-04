using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var production = builder.Configuration.GetValue<bool>("Deployment:Production");
var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../.."));
var certificateDirectory = Path.GetFullPath(builder.Configuration["Deployment:CertificateDirectory"] ?? Path.Combine(repositoryRoot, "artifacts/certificates"));
var httpsCertificateDirectory = Path.Combine(certificateDirectory, "https");
var protectionCertificateDirectory = Path.Combine(certificateDirectory, "dataprotection");
var settingsDirectory = Path.GetFullPath(builder.Configuration["Deployment:SettingsDirectory"] ?? Path.Combine(repositoryRoot, "artifacts/config"));
var keyDirectory = Path.Combine(repositoryRoot, "artifacts/dpkeys");
var frontendUrl = builder.Configuration["Deployment:FrontendUrl"] ?? "https://localhost:8443";
var networkCidr = builder.Configuration["Deployment:NetworkCidr"] ?? "172.28.90.0/24";
var httpsPort = builder.Configuration.GetValue("Deployment:HttpsPort", 8443);
var keyVolume = production ? "modular-monolith-production_dataprotection-keys" : "modular-monolith_dataprotection-keys";
var protectionApplicationName = builder.Configuration["Deployment:DataProtectionApplicationName"]
    ?? (production ? "ModularMonolith.Auth.Production" : "ModularMonolith.Auth.Local");

if (production && (new Uri(frontendUrl).IsLoopback || !frontendUrl.StartsWith("https://", StringComparison.Ordinal)))
{
    throw new InvalidOperationException("Production requires Deployment:FrontendUrl with the public HTTPS origin.");
}

builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(false)
    .ConfigureComposeFile(compose =>
    {
        // Both volumes survive Compose teardown and Aspire's path-based project names.
        foreach (var name in new[] { "modular-monolith_sqlserver-data", keyVolume })
        {
            compose.Volumes[name].External = true;
            compose.Volumes[name].Driver = null;
        }
        // Only this private Compose subnet may supply forwarded headers to ASP.NET.
        foreach (var network in compose.Networks.Values)
        {
            network.Ipam = new Ipam { Config = [new Dictionary<string, string> { ["subnet"] = networkCidr }] };
        }
    });

var jwtKey = builder.AddParameter("jwt-key",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);
var adminPassword = builder.AddParameter("admin-password",
    new GenerateParameterDefault { MinLength = 32, MinLower = 1, MinUpper = 1, MinNumeric = 1, MinSpecial = 1 }, secret: true, persist: true);
var certificatePassword = builder.AddParameter("dp-certificate-password", secret: true);
var administratorEmail = builder.Configuration["Deployment:AdministratorEmail"] ?? "admin@example.test";

var sql = builder.AddSqlServer("sqlserver")
    .WithImageTag("2025-latest")
    .WithEnvironment("MSSQL_PID", "Developer")
    .WithDataVolume(builder.ExecutionContext.IsPublishMode ? "modular-monolith_sqlserver-data" : null)
    .PublishAsDockerComposeService((_, service) =>
    {
        service.ContainerName = "modular-monolith_sqlserver_1";
        service.Ports.Clear();
        if (!production)
        {
            service.Ports.Add("127.0.0.1:14330:1433");
        }
        service.Healthcheck = new Healthcheck
        {
            Test = ["CMD-SHELL", "SQLCMDPASSWORD=\"$$MSSQL_SA_PASSWORD\" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q 'SELECT 1' -o /dev/null"],
            Interval = "5s",
            Timeout = "5s",
            Retries = 30,
            StartPeriod = "20s"
        };
    });
var database = sql.AddDatabase("database", "ModularMonolithDemo");
var authDatabase = sql.AddDatabase("authdb", "ModularMonolithAuth");
var tenant1 = sql.AddDatabase("tenant1", "Tenant1DB");
var tenant2 = sql.AddDatabase("tenant2", "Tenant2DB");

IResourceBuilder<ContainerResource>? mailpit = null;
IResourceBuilder<ParameterResource>? smtpHost = null;
if (!production)
{
    mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.29.4")
        .WithEndpoint(targetPort: 1025, name: "smtp", scheme: "tcp")
        .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "ui")
        .PublishAsDockerComposeService((_, service) =>
        {
            service.ContainerName = "modular-monolith_mailpit_1";
            service.Ports.Clear();
            service.Ports.Add("127.0.0.1:8025:8025");
        });
}
else
{
    smtpHost = builder.AddParameter("smtp-host");
}

var seed = builder.AddProject<Projects.ModularMonolith_Seed>("seed")
    .WithReference(database, "DefaultConnection")
    .WithReference(authDatabase, "AuthConnection")
    .WithEnvironment("Seed__AdministratorEmail", administratorEmail)
    .WithEnvironment("Seed__AdministratorPassword", adminPassword)
    .WithEnvironment("Seed__AdministratorName", "Platform administrator")
    .WaitFor(database).WaitFor(authDatabase).WaitFor(tenant1).WaitFor(tenant2)
    .PublishAsDockerFile(container => container.WithDockerfile("../..", stage: "seed")
        .WithVolume(keyVolume, "/app/dpkeys")
        .WithBindMount(protectionCertificateDirectory, "/app/certificates", isReadOnly: true)
        .WithBindMount(settingsDirectory, "/app/config", isReadOnly: true))
    .PublishAsDockerComposeService((_, service) =>
    {
        service.ContainerName = "modular-monolith_seed_1";
        service.Restart = "no";
        service.DependsOn["sqlserver"].Condition = "service_healthy";
    });

var api = builder.AddProject<Projects.ModularMonolith_WebApi>("webapi", launchProfileName: "http")
    .WithReference(database, "DefaultConnection")
    .WithReference(authDatabase, "AuthConnection")
    .WithEnvironment("ReverseProxy__KnownNetworks__0", networkCidr)
    .WithEnvironment("ReverseProxy__ForwardLimit", "1")
    .WithEnvironment("DOTNET_USE_POLLING_FILE_WATCHER", "1")
    .WithEnvironment("AllowedHosts", new Uri(frontendUrl).Host + ";localhost;webapi")
    .WaitForCompletion(seed)
    .PublishAsDockerFile(container => container.WithDockerfile("../..", stage: "webapi")
        .WithEndpoint("http", endpoint => endpoint.TargetPort = 8080)
        .WithVolume(keyVolume, "/app/dpkeys")
        .WithBindMount(protectionCertificateDirectory, "/app/certificates", isReadOnly: true)
        .WithBindMount(settingsDirectory, "/app/config", isReadOnly: true))
    .PublishAsDockerComposeService((_, service) =>
    {
        service.ContainerName = "modular-monolith_webapi_1";
        service.Ports.Clear();
        service.DependsOn["sqlserver"].Condition = "service_healthy";
    });

foreach (var application in new[] { seed, api })
{
    application
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.ExecutionContext.IsPublishMode ? "Production" : "Development")
        .WithEnvironment("DOTNET_ENVIRONMENT", builder.ExecutionContext.IsPublishMode ? "Production" : "Development")
        .WithEnvironment("Jwt__Key", jwtKey)
        .WithEnvironment("Jwt__KeyId", builder.Configuration["Deployment:JwtKeyId"] ?? "local-1")
        .WithEnvironment("Jwt__Issuer", "ModularMonolith")
        .WithEnvironment("Jwt__Audience", "ModularMonolith")
        .WithEnvironment("Tenants__tenant1", tenant1.Resource.ConnectionStringExpression)
        .WithEnvironment("Tenants__tenant2", tenant2.Resource.ConnectionStringExpression)
        .WithEnvironment("Auth__FrontendUrl", frontendUrl)
        .WithEnvironment("Auth__DefaultTenantId", "tenant1")
        .WithEnvironment("Auth__Tenants__0__Id", "tenant1")
        .WithEnvironment("Auth__Tenants__0__Name", "主要工作空間")
        .WithEnvironment("Auth__Tenants__1__Id", "tenant2")
        .WithEnvironment("Auth__Tenants__1__Name", "第二工作空間")
        .WithEnvironment("Auth__SettingsFile", builder.ExecutionContext.IsPublishMode ? "/app/config/authsettings.json" : Path.Combine(settingsDirectory, "authsettings.json"))
        .WithEnvironment("DataProtection__ApplicationName", protectionApplicationName)
        .WithEnvironment("DataProtection__KeyDirectory", builder.ExecutionContext.IsPublishMode ? "/app/dpkeys" : keyDirectory)
        .WithEnvironment("DataProtection__CertificatePath", builder.ExecutionContext.IsPublishMode ? "/app/certificates/dataprotection.pfx" : Path.Combine(protectionCertificateDirectory, "dataprotection.pfx"))
        .WithEnvironment("DataProtection__CertificatePassword", certificatePassword)
        .WithEnvironment("Smtp__FromAddress", builder.Configuration["Smtp:FromAddress"] ?? "noreply@example.test")
        .WithEnvironment("Smtp__FromName", builder.Configuration["Smtp:FromName"] ?? "Modular Monolith");
    if (mailpit is not null)
    {
        application.WithEnvironment("Smtp__Host", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Host))
            .WithEnvironment("Smtp__Port", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Port))
            .WithEnvironment("Smtp__EnableSsl", "false");
    }
    else
    {
        application.WithEnvironment("Smtp__Host", smtpHost!)
            .WithEnvironment("Smtp__Port", builder.Configuration["Smtp:Port"] ?? "587")
            .WithEnvironment("Smtp__EnableSsl", "true");
    }
}
if (production)
{
    var smtpUsername = builder.AddParameter("smtp-username", secret: true);
    var smtpPassword = builder.AddParameter("smtp-password", secret: true);
    api.WithEnvironment("Smtp__Username", smtpUsername).WithEnvironment("Smtp__Password", smtpPassword);
    seed.WithEnvironment("Smtp__Username", smtpUsername).WithEnvironment("Smtp__Password", smtpPassword);
}

builder.AddDockerfile("frontend", "../..", stage: "frontend")
    .WithBindMount(httpsCertificateDirectory, "/etc/nginx/certificates", isReadOnly: true)
    .WithEnvironment("API_UPSTREAM", api.GetEndpoint("http"))
    .WithHttpsEndpoint(port: httpsPort, targetPort: 8443, name: "https", isProxied: false)
    .WaitFor(api)
    .PublishAsDockerComposeService((_, service) =>
    {
        service.ContainerName = "modular-monolith_frontend_1";
        service.Ports.Clear();
        service.Ports.Add($"{(production ? "0.0.0.0" : "127.0.0.1")}:{httpsPort}:8443");
    });

builder.Build().Run();