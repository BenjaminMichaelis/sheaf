using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Sheaf.Core;
using Sheaf.Data;
using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

internal static class ProductionDatabaseHost
{
    public static IHost Create(string connectionString =
        "Server=127.0.0.1,1;Database=SheafProductionModelTests;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=1",
        bool membershipSecurity = true)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Production
        });
        builder.Configuration[$"ConnectionStrings:{ConnectionStrings.DatabaseKey}"] = connectionString;
        builder.AddDatabase();
        if (!membershipSecurity)
        {
            builder.Services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            builder.Services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            builder.Services.RemoveAll<DbContextOptions>();
            builder.Services.AddDbContext<ApplicationDbContext>(
                options => options.UseAzureSql(connectionString).UseMultiTenancy(),
                contextLifetime: ServiceLifetime.Scoped);
        }

        return builder.Build();
    }
}
