using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
                options.User.RequireUniqueEmail = true;
                options.ClaimsIdentity.RoleClaimType = SheafClaimTypes.Role;
                // Emails can contain any address EmailAddressAttribute accepts (apostrophes, plus signs), and the
                // user name is the email, so don't restrict user name characters.
                options.User.AllowedUserNameCharacters = "";
                // Six-digit codes instead of long link tokens: they can be typed on any device, so web and mobile
                // verify the same way without deep links. See EmailCodeTokenProvider for how guessing is bounded.
                options.Tokens.EmailConfirmationTokenProvider = EmailCodeTokenProvider.ProviderName;
                options.Tokens.PasswordResetTokenProvider = EmailCodeTokenProvider.ProviderName;
                options.Tokens.ChangeEmailTokenProvider = EmailCodeTokenProvider.ProviderName;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddTokenProvider<EmailCodeTokenProvider>(EmailCodeTokenProvider.ProviderName);
            builder.Services.TryAddSingleton(TimeProvider.System);

            return builder;
        }
    }
}
