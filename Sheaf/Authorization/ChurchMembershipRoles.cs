using Microsoft.EntityFrameworkCore;

using Sheaf.Core.Authentication;
using Sheaf.Data;

namespace Sheaf.Authorization;

/// <summary>
/// Reads the persisted role behind a principal's church selection. Authorization uses the stored role, not the role
/// claim, so a demotion or revocation applies on the next request instead of after the cookie's next revalidation.
/// </summary>
public interface IChurchMembershipRoles
{
    /// <summary>Returns the stored role of the selected membership, or null if it no longer belongs to the user.</summary>
    Task<ChurchMembershipRole?> GetRoleAsync(string userId, ChurchSelection selection, CancellationToken ct);
}

/// <summary>
/// Scoped, so one instance serves one request: the endpoint permission check and the tracker's current actor share a
/// single read. Nothing is kept beyond the request, so the next request always sees the latest stored role.
/// </summary>
public sealed class ChurchMembershipRoles(ApplicationDbContext db) : IChurchMembershipRoles
{
    private readonly Dictionary<(string UserId, Guid ChurchId, Guid MembershipId), ChurchMembershipRole?> _read = [];

    public async Task<ChurchMembershipRole?> GetRoleAsync(string userId, ChurchSelection selection, CancellationToken ct)
    {
        var key = (userId, selection.ChurchId, selection.MembershipId);
        if (_read.TryGetValue(key, out var role))
            return role;
        role = await db.ChurchMemberships.AsNoTracking()
            .Where(m => m.ChurchId == selection.ChurchId && m.Id == selection.MembershipId && m.UserId == userId)
            .Select(m => (ChurchMembershipRole?)m.Role)
            .SingleOrDefaultAsync(ct);
        _read[key] = role;
        return role;
    }
}
