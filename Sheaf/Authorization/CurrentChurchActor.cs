using System.Security.Claims;

using Sheaf.Core.Authentication;
using Sheaf.Data;

namespace Sheaf.Authorization;

public sealed record ChurchActor(ClaimsPrincipal Principal, Guid ChurchId, Guid MembershipId, string UserId, ChurchMembershipRole Role);

public interface ICurrentChurchActor
{
    Task<ChurchActor?> GetAsync(CancellationToken ct);
}

/// <summary>
/// The signed-in user's selected membership with its persisted role. It reads through <see cref="IChurchMembershipRoles"/>,
/// so it reuses the read the endpoint permission check already made in this request.
/// </summary>
public sealed class HttpCurrentChurchActor(IChurchMembershipRoles memberships, ICurrentChurch church, IHttpContextAccessor http) : ICurrentChurchActor
{
    public async Task<ChurchActor?> GetAsync(CancellationToken ct)
    {
        var principal = http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || principal.FindFirstValue(ClaimTypes.NameIdentifier) is not { } userId
            || principal.ReadChurchClaims(out ChurchSelection? selection) != ChurchClaimState.Selected
            || selection is null
            // The request's tenant must be the church the claims selected.
            || church.ChurchId != selection.ChurchId || church.LeaderId != selection.MembershipId)
            return null;

        return await memberships.GetRoleAsync(userId, selection, ct) is { } role
            ? new(principal, selection.ChurchId, selection.MembershipId, userId, role)
            : null;
    }
}
