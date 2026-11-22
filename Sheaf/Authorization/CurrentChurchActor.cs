using System.Security.Claims;

using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

namespace Sheaf.Authorization;

public sealed record ChurchActor(ClaimsPrincipal Principal, Guid ChurchId, Guid MembershipId, string UserId, ChurchMembershipRole Role);

public interface ICurrentChurchActor
{
    Task<ChurchActor?> GetAsync(CancellationToken ct);
}

public sealed class HttpCurrentChurchActor(ApplicationDbContext db, ICurrentChurch church, IHttpContextAccessor http) : ICurrentChurchActor
{
    public async Task<ChurchActor?> GetAsync(CancellationToken ct)
    {
        var principal = http.HttpContext?.User;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal?.Identity?.IsAuthenticated != true || userId is null
            || church.ChurchId is not { } churchId || church.LeaderId is not { } membershipId)
            return null;

        var membership = await db.ChurchMemberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.ChurchId == churchId && m.Id == membershipId && m.UserId == userId, ct);
        return membership is null ? null : new(principal, churchId, membership.Id, membership.UserId, membership.Role);
    }
}
