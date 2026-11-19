using Microsoft.AspNetCore.Authorization;

using Sheaf.Core.Authentication;

namespace Sheaf.Authorization;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddSheafAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            AuthorizationPolicy authenticated = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            AuthorizationPolicy churchMember = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(SheafClaimTypes.ChurchId)
                .Build();

            options.AddPolicy(SheafPolicies.Authenticated, authenticated);
            options.AddPolicy(SheafPolicies.ChurchMember, churchMember);
            options.DefaultPolicy = authenticated;
            options.FallbackPolicy = churchMember;
        });
        services.AddSingleton<IAuthorizationPolicyProvider, SheafAuthorizationPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }

    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(SheafPolicies.ForPermission(permission));
}
