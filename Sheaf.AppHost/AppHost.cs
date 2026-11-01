using Aspire.Hosting.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;

using Sheaf.AppHost;
using Sheaf.Core;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("Sheaf-cae");

var docsGroup = builder.AddLogicalGroup("docs");
builder.AddAspireDocs().WithParentRelationship(docsGroup);
var authSigningKey = builder.AddParameter("auth-signing-key", secret: true);

IResourceBuilder<IResourceWithConnectionString> db;
IResourceBuilder<SqlServerServerResource>? sql = null;


if (builder.ExecutionContext.IsPublishMode)
{
    db = builder.AddAzureSqlServer().AddDatabase("Sheaf-db");
}
else
{
    sql = builder.AddSqlServer();
    db = sql.AddSqlDatabase();

    var disableDbGate = string.Equals(
        builder.Configuration["DisableDbGate"],
        "true",
        StringComparison.OrdinalIgnoreCase);

    if (!disableDbGate)
    {
        // DBGate is a database viewer.
        var dbGate = builder.AddContainer("dbgate", "dbgate/dbgate")
            .ExcludeFromManifest()
            .ExcludeFromMcp()
            .WithExplicitStart()
            .WithLifetime(ContainerLifetime.Persistent)
            .WithContainerName("Sheaf-db-gate")
            .WithHttpEndpoint(targetPort: 3000)
            .WaitFor(sql)
            .WithEnvironment("CONNECTIONS", "mssql")
            .WithEnvironment("LABEL_mssql", "MS SQL")
            .WithEnvironment("SERVER_mssql", "host.docker.internal")
            .WithEnvironment("PORT_mssql", () => $"{sql.Resource.PrimaryEndpoint.Port}")
            .WithEnvironment("USER_mssql", "sa")
            .WithEnvironment("PASSWORD_mssql", sql.Resource.PasswordParameter)
            .WithEnvironment("ENGINE_mssql", "mssql@dbgate-plugin-mssql")
            .WithParentRelationship(sql)
            .WithHttpHealthCheck("/");
    }
}

var backend = builder.AddProject<Projects.Sheaf>(
        "Sheaf-backend",
        options => options.ExcludeLaunchProfile = true)
    .WaitFor(db)
    .PublishAsAzureContainerApp((infra, app) => app.Template.Scale.MaxReplicas = 1);

var backendMigrations = backend
    .AddEFMigrations(
        "Sheaf-backend-migrations",
        "Sheaf.Data.ApplicationDbContext")
    .WithMigrationsProject("../Sheaf.Data/Sheaf.Data.csproj")
    .RunDatabaseUpdateOnStart()
    .WithReference(db)
    .PublishAsMigrationScript()
    .PublishAsMigrationBundle(publishContainer: true);

backend.WithEnvironment("Auth__SigningKey", authSigningKey);

if (!builder.ExecutionContext.IsPublishMode)
{
    backend.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
    backend.WithEnvironment("DOTNET_ENVIRONMENT", "Development");
    backend.WithEnvironment(async context =>
    {
        context.EnvironmentVariables[$"ConnectionStrings__{ConnectionStrings.DatabaseKey}"] =
            await db.Resource.GetConnectionStringAsync(context.CancellationToken)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStrings.DatabaseKey}' could not be resolved.");
    });

    // RunDatabaseUpdateOnStart uses the backend project as the EF startup project, which reads
    // ConnectionStrings__Database. WithReference(db) injects ConnectionStrings__Sheaf-db
    // (keyed by resource name), so we also inject the expected key explicitly.
    backendMigrations.WithEnvironment(async context =>
    {
        context.EnvironmentVariables[$"ConnectionStrings__{ConnectionStrings.DatabaseKey}"] =
            await db.Resource.GetConnectionStringAsync(context.CancellationToken)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStrings.DatabaseKey}' could not be resolved.");
    });
    // The backend startup project (Program.cs) validates Auth:SigningKey at startup, so it must
    // be available when EF runs database update against the backend startup project.
    backendMigrations.WithEnvironment("Auth__SigningKey", authSigningKey);
}
else
{
    backend.WithEnvironment($"ConnectionStrings__{ConnectionStrings.DatabaseKey}", db);
}

backend.WithHttpEndpoint(port: 5285, targetPort: 5285, name: "http", env: "ASPNETCORE_HTTP_PORTS", isProxied: false);
backend.WithHttpHealthCheck("/alive");

if (builder.ExecutionContext.IsPublishMode)
{
    backend.WithExternalHttpEndpoints();
}

if (sql is not null)
{
    backendMigrations.WaitFor(sql);
}

backend.WaitForCompletion(backendMigrations);


builder.Build().Run();
