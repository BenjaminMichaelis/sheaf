using Microsoft.EntityFrameworkCore;

using Sheaf.Data;
using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Authentication;

internal sealed class ChurchAccessService(
    ChurchMembershipDirectory directory,
    ApplicationDbContext db,
    ICurrentChurch currentChurch,
    ITenantContextInitializer initializer) : IChurchAccessService
{
    public Task<IReadOnlyList<ChurchInfo>> GetChurchesForUserAsync(
        ApplicationUser user, CancellationToken cancellationToken = default) =>
        directory.GetChurchesForUserAsync(user.Id, cancellationToken);

    public async Task<ChurchSelection?> ResolveSignInAsync(
        ApplicationUser user, CancellationToken cancellationToken = default)
    {
        InitializeTenantContext(null, null);
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

        InitializeTenantContext(null, null);
        return Task.FromResult<ChurchSelection?>(null);
    }

    public async Task<ChurchSwitchResult> SwitchChurchAsync(
        ApplicationUser user, Guid churchId, CancellationToken cancellationToken = default)
    {
        if (db.ChangeTracker.Entries<ITenanted>().Any())
        {
            return new ChurchSwitchResult(ChurchSwitchFailure.TenantedEntitiesTracked, null);
        }

        if (churchId == Guid.Empty)
        {
            return new ChurchSwitchResult(ChurchSwitchFailure.ChurchNotAccessible, null);
        }

        Guid? previousChurchId = currentChurch.ChurchId;
        Guid? previousMembershipId = currentChurch.LeaderId;
        try
        {
            InitializeTenantContext(churchId, null);
            ChurchSelection? selection = await FindMembershipAsync(user, churchId, null, cancellationToken);
            if (selection is null)
            {
                InitializeTenantContext(previousChurchId, previousMembershipId);
                return new ChurchSwitchResult(ChurchSwitchFailure.ChurchNotAccessible, null);
            }

            InitializeTenantContext(selection.ChurchId, selection.MembershipId);
            return new ChurchSwitchResult(ChurchSwitchFailure.None, selection);
        }
        catch
        {
            InitializeTenantContext(previousChurchId, previousMembershipId);
            throw;
        }
    }

    private async Task<ChurchSelection?> RecheckMembershipAsync(
        ApplicationUser user, Guid churchId, Guid? membershipId, CancellationToken cancellationToken)
    {
        InitializeTenantContext(churchId, null);
        ChurchSelection? selection = await FindMembershipAsync(user, churchId, membershipId, cancellationToken);

        InitializeTenantContext(selection?.ChurchId, selection?.MembershipId);
        return selection;
    }

    private async Task<ChurchSelection?> FindMembershipAsync(
        ApplicationUser user, Guid churchId, Guid? membershipId, CancellationToken cancellationToken)
    {
        ChurchSelection? selection = await db.ChurchMemberships
            .Where(membership => membership.ChurchId == churchId
                && membership.UserId == user.Id
                && (membershipId == null || membership.Id == membershipId))
            .Select(membership => new ChurchSelection(membership.ChurchId, membership.Id, membership.Role))
            .SingleOrDefaultAsync(cancellationToken);

        return selection is not null && Enum.IsDefined(selection.Role) ? selection : null;
    }

    private void InitializeTenantContext(Guid? churchId, Guid? membershipId)
    {
        // Audited tenant selection follows a rechecked membership or a validated cookie.
#pragma warning disable RS0030
        initializer.Initialize(churchId, membershipId);
#pragma warning restore RS0030
    }
}
