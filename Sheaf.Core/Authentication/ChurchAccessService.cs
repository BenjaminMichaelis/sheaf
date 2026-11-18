using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

namespace Sheaf.Core.Authentication;

internal sealed class ChurchAccessService(
    ChurchMembershipDirectory directory,
    ApplicationDbContext db,
    ITenantContextInitializer initializer) : IChurchAccessService
{
    public Task<IReadOnlyList<ChurchInfo>> GetChurchesForUserAsync(
        ApplicationUser user, CancellationToken cancellationToken = default) =>
        directory.GetChurchesForUserAsync(user.Id, cancellationToken);

    public async Task<ChurchSelection?> ResolveSignInAsync(
        ApplicationUser user, CancellationToken cancellationToken = default)
    {
        initializer.Initialize(null, null);
        IReadOnlyList<ChurchInfo> churches = await GetChurchesForUserAsync(user, cancellationToken);
        return churches.Count == 1
            ? await RecheckMembershipAsync(user, churches[0].ChurchId, null, cancellationToken)
            : null;
    }

    public Task<ChurchSelection?> ResolveRenewalAsync(
        ApplicationUser user, ChurchSelection? selection, CancellationToken cancellationToken = default)
    {
        if (selection is not null)
        {
            return RecheckMembershipAsync(user, selection.ChurchId, selection.MembershipId, cancellationToken);
        }

        initializer.Initialize(null, null);
        return Task.FromResult<ChurchSelection?>(null);
    }

    private async Task<ChurchSelection?> RecheckMembershipAsync(
        ApplicationUser user, Guid churchId, Guid? membershipId, CancellationToken cancellationToken)
    {
        initializer.Initialize(churchId, null);
        ChurchSelection? selection = await db.ChurchMemberships
            .Where(membership => membership.UserId == user.Id
                && (membershipId == null || membership.Id == membershipId))
            .Select(membership => new ChurchSelection(membership.ChurchId, membership.Id, membership.Role))
            .SingleOrDefaultAsync(cancellationToken);

        if (selection is not null && !Enum.IsDefined(selection.Role))
        {
            selection = null;
        }

        initializer.Initialize(selection?.ChurchId, selection?.MembershipId);
        return selection;
    }
}
