using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public static class DatabaseConfiguration
{
    /// <summary>Registers the model-affecting database settings shared by runtime and design-time contexts.</summary>
    public static IServiceCollection AddSheafDatabase(this IServiceCollection services, string connectionString)
    {
        services.Configure<IdentityOptions>(options =>
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        services.AddDbContext<ApplicationDbContext>(
            options => options.UseAzureSql(connectionString).UseMultiTenancy(),
            contextLifetime: ServiceLifetime.Scoped);

        return services;
    }
}
