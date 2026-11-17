using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sheaf.Core;
using Sheaf.Data;

namespace Sheaf.AppHost;

public class DesignTimeAppDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var connectionString = configuration.GetConnectionString(ConnectionStrings.DatabaseKey)
            ?? $"Server=(localdb)\\mssqllocaldb;Database={nameof(Sheaf)}DesignTime;Trusted_Connection=True;TrustServerCertificate=True";

        // Note: the ServiceProvider is intentionally not disposed here; this is design-time
        // code that runs in a short-lived dotnet-ef process.
        var services = new ServiceCollection();
        services.AddSheafDatabase(connectionString);
        return services.BuildServiceProvider().GetRequiredService<ApplicationDbContext>();
    }
}
