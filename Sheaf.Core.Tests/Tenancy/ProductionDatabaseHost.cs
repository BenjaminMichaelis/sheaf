using Microsoft.Extensions.Hosting;

using Sheaf.Core;

namespace Sheaf.Core.Tests;

internal static class ProductionDatabaseHost
{
    public static IHost Create(string connectionString =
        "Server=127.0.0.1,1;Database=SheafProductionModelTests;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=1")
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Production
        });
        builder.Configuration[$"ConnectionStrings:{ConnectionStrings.DatabaseKey}"] = connectionString;
        builder.AddDatabase();
        return builder.Build();
    }
}
