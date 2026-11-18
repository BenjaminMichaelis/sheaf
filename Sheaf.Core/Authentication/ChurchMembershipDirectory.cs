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

        ChurchInfo[] churches = await (
            from membership in db.ChurchMemberships.IgnoreQueryFilters([MultiTenancyConvention.FilterName])
            where membership.UserId == userId
            join church in db.Churches on membership.ChurchId equals church.Id
            orderby church.Name, church.Id
            select new ChurchInfo(membership.ChurchId, church.Name, membership.Role))
            .ToArrayAsync(cancellationToken);

        LogDirectoryRead(logger, userId, churches.Length);
        return churches;
    }

    [LoggerMessage(EventId = 15600, EventName = "ChurchMembershipDirectoryRead", Level = LogLevel.Information,
        Message = "Read church membership directory for user {UserId}; returned {ChurchCount} churches.")]
    private static partial void LogDirectoryRead(ILogger logger, string userId, int churchCount);
}
