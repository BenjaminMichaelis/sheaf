using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

using Sheaf.Core.Authentication;

namespace Sheaf.Authorization;

public sealed class SheafAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(SheafPolicies.PermissionPrefix, StringComparison.Ordinal))
        {
            return await _fallback.GetPolicyAsync(policyName);
        }

        string name = policyName[SheafPolicies.PermissionPrefix.Length..];
        AuthorizationPolicyBuilder builder = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(SheafClaimTypes.ChurchId);

        if (Enum.TryParse(name, out Permission permission) && Enum.IsDefined(permission) && name == permission.ToString())
        {
            builder.AddRequirements(new PermissionRequirement(permission));
        }
        else
        {
            builder.RequireAssertion(_ => false);
        }

        return builder.Build();
    }
}
