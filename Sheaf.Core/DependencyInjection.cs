using Microsoft.AspNetCore.Identity;
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

            builder.Services.AddScoped<TenantContext>();
            builder.Services.AddScoped<ICurrentChurch>(services => services.GetRequiredService<TenantContext>());
            builder.Services.AddSheafDatabase(connectionString);

            if (builder.Environment.IsDevelopment())
            {
                builder.Services.AddDatabaseDeveloperPageExceptionFilter();
            }

            builder.Services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

            return builder;
        }
    }
}
