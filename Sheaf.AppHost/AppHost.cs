using Aspire.Hosting.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;

using Sheaf.AppHost;
using Sheaf.Core;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("Sheaf-cae");

var docsGroup = builder.AddLogicalGroup("docs");
builder.AddAspireDocs().WithParentRelationship(docsGroup);

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

var backend = builder.AddDotnetProject(
        "Sheaf-backend",
        "../Sheaf/Sheaf.csproj",
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

if (!builder.ExecutionContext.IsPublishMode)
{
    // Captures every email the API sends locally; open its web UI from the dashboard to read verification codes.
    // Deployed environments configure a real server through Email:Smtp:* settings instead.
    var mailpit = builder.AddMailPit("mailpit");
    backend.WithReference(mailpit).WaitFor(mailpit);

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

    // Planning Center is optional: an unset optional parameter is not prompted for and its variable is omitted, so
    // Sheaf reports the integration unconfigured. Production sets these through Infra (planning_center).
    // https://aspire.dev/fundamentals/external-parameters/#optional-parameters
    backend
        .WithEnvironment("PlanningCenter__ClientId", builder.AddParameter("planning-center-client-id").AsOptional())
        .WithEnvironment("PlanningCenter__ClientSecret",
            builder.AddParameter("planning-center-client-secret", secret: true).AsOptional())
        .WithEnvironment("PlanningCenter__RedirectUri", builder.AddParameter("planning-center-redirect-uri").AsOptional())
        .WithEnvironment("PlanningCenter__ReturnUrl", builder.AddParameter("planning-center-return-url").AsOptional())
        .WithEnvironment("PlanningCenter__UserAgent", builder.AddParameter("planning-center-user-agent").AsOptional());
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

#pragma warning disable ASPIREBROWSERLOGS001
builder.AddJavaScriptApp("Sheaf-frontend", "../Sheaf.Web", "dev")
    .WithPnpm(install: true)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithDependency(backend)
    .WithEnvironment("APP_BACKEND_HTTP", backend.GetEndpoint("http"));
#pragma warning restore ASPIREBROWSERLOGS001


builder.Build().Run();
