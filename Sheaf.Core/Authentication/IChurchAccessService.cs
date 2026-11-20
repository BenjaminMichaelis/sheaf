using Sheaf.Data;

namespace Sheaf.Core.Authentication;

/// <summary>Resolves church access for a user already validated by Identity, without transport-specific selection rules.</summary>
public interface IChurchAccessService
{
    Task<IReadOnlyList<ChurchInfo>> GetChurchesForUserAsync(
        ApplicationUser user, CancellationToken cancellationToken = default);

    Task<ChurchSelection?> ResolveSignInAsync(
        ApplicationUser user, CancellationToken cancellationToken = default);

    Task<ChurchSelection?> ResolveRenewalAsync(
        ApplicationUser user, ChurchSelection? selection, CancellationToken cancellationToken = default);

    Task<ChurchSwitchResult> SwitchChurchAsync(
        ApplicationUser user, Guid churchId, CancellationToken cancellationToken = default);
}

public enum ChurchSwitchFailure
{
    None,
    ChurchNotAccessible,
    TenantedEntitiesTracked
}

public sealed record ChurchSwitchResult(ChurchSwitchFailure Failure, ChurchSelection? Selection);
