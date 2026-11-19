using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sheaf.Data;
using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Authentication;

internal sealed partial class ChurchMembershipDirectory(
    ApplicationDbContext db,
    ILogger<ChurchMembershipDirectory> logger)
{
    public async Task<IReadOnlyList<ChurchInfo>> GetChurchesForUserAsync(
        string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // Audited tenancy bypass: sign-in must list memberships across churches before one is selected.
#pragma warning disable RS0030
        ChurchInfo[] churches = await db.ChurchMemberships
            .IgnoreQueryFilters([MultiTenancyConvention.FilterName])
#pragma warning restore RS0030
            .Where(membership => membership.UserId == userId)
            .Join(db.Churches,
                membership => membership.ChurchId,
                church => church.Id,
                (membership, church) => new { Membership = membership, Church = church })
            .OrderBy(result => result.Church.Name)
            .ThenBy(result => result.Church.Id)
            .Select(result => new ChurchInfo(result.Membership.ChurchId, result.Church.Name, result.Membership.Role))
            .ToArrayAsync(cancellationToken);

        LogDirectoryRead(logger, userId, churches.Length);
        return churches;
    }

    [LoggerMessage(EventId = 15600, EventName = "ChurchMembershipDirectoryRead", Level = LogLevel.Information,
        Message = "Read church membership directory for user {UserId}; returned {ChurchCount} churches.")]
    private static partial void LogDirectoryRead(ILogger logger, string userId, int churchCount);
}
