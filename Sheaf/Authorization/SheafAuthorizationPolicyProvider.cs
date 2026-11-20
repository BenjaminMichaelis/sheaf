using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Sheaf.Authorization;

public sealed class SheafAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public bool AllowsCachingPolicies => true;

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(SheafPolicies.PermissionPrefix, StringComparison.Ordinal))
        {
            return await _fallback.GetPolicyAsync(policyName);
        }

        string name = policyName[SheafPolicies.PermissionPrefix.Length..];
        AuthorizationPolicy churchMember = await _fallback.GetPolicyAsync(SheafPolicies.ChurchMember)
            ?? throw new InvalidOperationException($"The '{SheafPolicies.ChurchMember}' policy is not registered.");
        AuthorizationPolicyBuilder builder = new(churchMember);

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
