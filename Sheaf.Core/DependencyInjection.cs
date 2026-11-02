using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheaf.Data;

namespace Sheaf.Core;

public static class DependencyInjection
{
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        public TBuilder AddDatabase()
        {
            var connectionString = builder.Configuration.GetConnectionString(ConnectionStrings.DatabaseKey)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionStrings.DatabaseKey}' not found.");

            void BuildDbOptions(DbContextOptionsBuilder options)
            {
                options.UseAzureSql(connectionString);
            }

            builder.Services.AddScoped<TenantContext>();
            builder.Services.AddScoped<ITenantContext>(services => services.GetRequiredService<TenantContext>());
            builder.Services.AddDbContext<ApplicationDbContext>(
                BuildDbOptions,
                contextLifetime: ServiceLifetime.Scoped);

            if (builder.Environment.IsDevelopment())
            {
                builder.Services.AddDatabaseDeveloperPageExceptionFilter();
            }

            builder.Services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

            return builder;
        }
    }
}
