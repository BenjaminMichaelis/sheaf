using System.Security.Claims;

using Sheaf.Data;

namespace Sheaf.Core.Authentication;

public enum ChurchClaimState
{
    Unselected,
    Selected,
    Invalid
}

public static class ChurchPrincipalExtensions
{
    public static ChurchClaimState ReadChurchClaims(this ClaimsPrincipal principal, out ChurchSelection? selection)
    {
        selection = null;
        Claim[] churchIds = principal.FindAll(SheafClaimTypes.ChurchId).ToArray();
        Claim[] membershipIds = principal.FindAll(SheafClaimTypes.ChurchMembershipId).ToArray();
        Claim[] roles = principal.FindAll(SheafClaimTypes.Role).ToArray();

        if (principal.HasClaim(claim => claim.Type == ClaimTypes.Role))
        {
            return ChurchClaimState.Invalid;
        }

        if (churchIds.Length == 0 && membershipIds.Length == 0 && roles.Length == 0)
        {
            return ChurchClaimState.Unselected;
        }

        if (churchIds.Length != 1 || membershipIds.Length != 1 || roles.Length != 1
            || principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity
            || churchIds[0].Subject != identity
            || churchIds[0].Subject != membershipIds[0].Subject || churchIds[0].Subject != roles[0].Subject
            || !Guid.TryParseExact(churchIds[0].Value, "D", out Guid churchId) || churchId == Guid.Empty
            || !Guid.TryParseExact(membershipIds[0].Value, "D", out Guid membershipId) || membershipId == Guid.Empty
            || !Enum.TryParse(roles[0].Value, out ChurchMembershipRole role)
            || !Enum.IsDefined(role) || roles[0].Value != role.ToString())
        {
            return ChurchClaimState.Invalid;
        }

        selection = new ChurchSelection(churchId, membershipId, role);
        return ChurchClaimState.Selected;
    }
}
