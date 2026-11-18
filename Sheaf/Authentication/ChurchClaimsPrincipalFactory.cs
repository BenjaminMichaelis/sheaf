using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Sheaf.Core.Authentication;
using Sheaf.Data;

namespace Sheaf.Authentication;

internal sealed class ChurchClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> options,
    IChurchAccessService churchAccess,
    IHttpContextAccessor httpContextAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, options)
{
    internal ClaimsPrincipal? RenewalPrincipal { get; set; }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user);
        foreach (Claim claim in identity.Claims.Where(claim => claim.Type == Options.ClaimsIdentity.RoleClaimType
            || claim.Type is SheafClaimTypes.ChurchId or SheafClaimTypes.ChurchMembershipId or SheafClaimTypes.Role or ClaimTypes.Role)
            .ToArray())
        {
            identity.RemoveClaim(claim);
        }

        CancellationToken cancellationToken = httpContextAccessor.HttpContext?.RequestAborted ?? default;
        ChurchSelection? selection;
        if (RenewalPrincipal is { } renewalPrincipal)
        {
            renewalPrincipal.ReadChurchClaims(out ChurchSelection? previousSelection);
            selection = await churchAccess.ResolveRenewalAsync(
                user, UserManager.GetUserId(renewalPrincipal) == user.Id ? previousSelection : null, cancellationToken);
        }
        else
        {
            selection = await churchAccess.ResolveSignInAsync(user, cancellationToken);
        }

        if (selection is not null)
        {
            identity.AddClaim(new Claim(SheafClaimTypes.ChurchId, selection.ChurchId.ToString("D")));
            identity.AddClaim(new Claim(SheafClaimTypes.ChurchMembershipId, selection.MembershipId.ToString("D")));
            identity.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, selection.Role.ToString()));
        }

        return identity;
    }
}
