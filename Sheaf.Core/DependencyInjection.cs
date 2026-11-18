using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheaf.Core.Authentication;
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

            builder.Services.AddSheafDatabase(connectionString);
            builder.Services.AddScoped<ChurchMembershipDirectory>();
            builder.Services.AddScoped<IChurchAccessService, ChurchAccessService>();

            if (builder.Environment.IsDevelopment())
            {
                builder.Services.AddDatabaseDeveloperPageExceptionFilter();
            }

            builder.Services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.ClaimsIdentity.RoleClaimType = SheafClaimTypes.Role;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

            return builder;
        }
    }
}
