using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Docker.Resources.ServiceNodes;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(false)
    .ConfigureComposeFile(compose =>
    {
        // Keep database storage independent of Aspire's path-based Compose project name.
        var volume = compose.Volumes["modular-monolith_sqlserver-data"];
        volume.External = true;
        volume.Driver = null;
    });

var jwtKey = builder.AddParameter("jwt-key",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);

var sql = builder.AddSqlServer("sqlserver")
    .WithImageTag("2025-latest")
    .WithEnvironment("MSSQL_PID", "Developer")
    .WithDataVolume(builder.ExecutionContext.IsPublishMode ? "modular-monolith_sqlserver-data" : null)
    .PublishAsDockerComposeService((_, service) =>
    {
        service.ContainerName = "modular-monolith_sqlserver_1";
        service.Ports.Add("127.0.0.1:14330:1433");
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
var tenant1 = sql.AddDatabase("tenant1", "Tenant1DB");
var tenant2 = sql.AddDatabase("tenant2", "Tenant2DB");

var seed = builder.AddProject<Projects.ModularMonolith_Seed>("seed")
    .WithReference(database, "DefaultConnection")
    .WaitFor(database)
    .PublishAsDockerFile(container => container.WithDockerfile("../..", stage: "seed"))
    .PublishAsDockerComposeService((_, service) =>
    {
        service.ContainerName = "modular-monolith_seed_1";
        service.Restart = "no";
        service.DependsOn["sqlserver"].Condition = "service_healthy";
    });

builder.AddProject<Projects.ModularMonolith_WebApi>("webapi", launchProfileName: "http")
    .WithReference(database, "DefaultConnection")
    .WithEnvironment("Jwt__Key", jwtKey)
    .WithEnvironment("Tenants__tenant1", tenant1.Resource.ConnectionStringExpression)
    .WithEnvironment("Tenants__tenant2", tenant2.Resource.ConnectionStringExpression)
    .WaitFor(tenant1)
    .WaitFor(tenant2)
    .WaitForCompletion(seed)
    .PublishAsDockerFile(container => container
        .WithDockerfile("../..", stage: "webapi")
        .WithEndpoint("http", endpoint => endpoint.TargetPort = 8080))
    .PublishAsDockerComposeService((_, service) =>
    {
        service.ContainerName = "modular-monolith_webapi_1";
        service.Ports.Add("127.0.0.1:8080:8080");
        service.DependsOn["sqlserver"].Condition = "service_healthy";
    });

builder.Build().Run();
