using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

/// <summary>
/// A church's connection to one external system, such as a church management system. A church has at most one row per
/// provider, kept after a disconnect so a reconnect reuses it. Provider-specific secrets live only in the encrypted
/// <see cref="ProtectedCredentials"/>, so adding a provider never needs new columns.
/// </summary>
public sealed class ChurchIntegration : ITenanted
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public IntegrationProvider Provider { get; set; }

    public IntegrationStatus Status { get; set; }

    /// <summary>
    /// The provider's id for the church's account there (Planning Center's organization), or null while disconnected.
    /// Uniqueness across churches is enforced by <see cref="IntegrationTenantLink"/>, not here, so this tenanted row has
    /// no cross-church index.
    /// </summary>
    [MaxLength(100)]
    public string? ExternalTenantId { get; set; }

    [MaxLength(200)]
    public string? ExternalTenantName { get; set; }

    /// <summary>The provider account whose permissions the integration acts with.</summary>
    [MaxLength(100)]
    public string? ConnectedAccountId { get; set; }

    [MaxLength(200)]
    public string? ConnectedAccountName { get; set; }

    /// <summary>The Sheaf membership that connected the church. Not a foreign key, so the record survives the membership.</summary>
    public Guid? ConnectedByMembershipId { get; set; }

    public DateTimeOffset? ConnectedAt { get; set; }

    [MaxLength(200)]
    public string? Scopes { get; set; }

    /// <summary>
    /// The provider's credentials (OAuth tokens, an API key, ...) serialized and encrypted with Data Protection, or null
    /// when there are none. Never store or log the plain value.
    /// </summary>
    public string? ProtectedCredentials { get; set; }

    /// <summary>Changes on every write, so concurrent credential refreshes cannot overwrite each other.</summary>
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}

/// <summary>
/// The global registry of which church each external account is connected to. It is deliberately not tenanted, like
/// <see cref="Church.Slug"/>: its only job is to stop two churches from importing the same external account, which no
/// per-church table can check. It holds identifiers only.
/// </summary>
[PrimaryKey(nameof(Provider), nameof(ExternalTenantId))]
[Index(nameof(Provider), nameof(ChurchId), IsUnique = true)]
public sealed class IntegrationTenantLink
{
    public IntegrationProvider Provider { get; set; }

    [MaxLength(100)]
    public required string ExternalTenantId { get; set; }

    public Guid ChurchId { get; set; }
}

/// <summary>An external system Sheaf can connect to. Stored by name, so values can be added in any order.</summary>
public enum IntegrationProvider
{
    PlanningCenter = 1
}

public enum IntegrationStatus
{
    Disconnected = 0,
    Active = 1,
    /// <summary>The provider rejected the stored credentials; an admin must connect again.</summary>
    NeedsReauthorization = 2
}
