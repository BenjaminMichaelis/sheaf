using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

        // Mirror the Identity schema version used at runtime so that generated migrations
        // include all tables (e.g. AspNetUserPasskeys added by Version3).
        // Note: the ServiceProvider is intentionally not disposed here; this is design-time
        // code that runs in a short-lived dotnet-ef process.
        var services = new ServiceCollection();
        services.Configure<IdentityOptions>(options =>
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        services.AddDbContext<ApplicationDbContext>(options => options.UseAzureSql(connectionString));
        return services.BuildServiceProvider().GetRequiredService<ApplicationDbContext>();
    }
}
