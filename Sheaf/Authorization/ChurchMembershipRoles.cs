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

public sealed class ChurchMembershipRoles(ApplicationDbContext db) : IChurchMembershipRoles
{
    public async Task<ChurchMembershipRole?> GetRoleAsync(string userId, ChurchSelection selection, CancellationToken ct) =>
        await db.ChurchMemberships.AsNoTracking()
            .Where(m => m.ChurchId == selection.ChurchId && m.Id == selection.MembershipId && m.UserId == userId)
            .Select(m => (ChurchMembershipRole?)m.Role)
            .SingleOrDefaultAsync(ct);
}
