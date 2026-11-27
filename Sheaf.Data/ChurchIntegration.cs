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
    public const int ExternalIdMaxLength = 100;
    public const int NameMaxLength = 200;

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid ChurchId { get; set; }

    public IntegrationProvider Provider { get; set; }

    public IntegrationStatus Status { get; private set; }

    /// <summary>
    /// The provider's id for the church's account there (Planning Center's organization), or null while disconnected.
    /// Uniqueness across churches is enforced by <see cref="IntegrationTenantLink"/>, not here, so this tenanted row has
    /// no cross-church index.
    /// </summary>
    [MaxLength(ExternalIdMaxLength)]
    public string? ExternalTenantId { get; private set; }

    [MaxLength(NameMaxLength)]
    public string? ExternalTenantName { get; private set; }

    /// <summary>The provider account whose permissions the integration acts with.</summary>
    [MaxLength(ExternalIdMaxLength)]
    public string? ConnectedAccountId { get; private set; }

    [MaxLength(NameMaxLength)]
    public string? ConnectedAccountName { get; private set; }

    /// <summary>The Sheaf membership that connected the church. Not a foreign key, so the record survives the membership.</summary>
    public Guid? ConnectedByMembershipId { get; private set; }

    public DateTimeOffset? ConnectedAt { get; private set; }

    [MaxLength(NameMaxLength)]
    public string? Scopes { get; private set; }

    /// <summary>
    /// The provider's credentials (OAuth tokens, an API key, ...) serialized and encrypted with Data Protection, or null
    /// when there are none. Never store or log the plain value.
    /// </summary>
    public string? ProtectedCredentials { get; private set; }

    /// <summary>
    /// Changes on every write, so two concurrent writes cannot silently overwrite each other; the later save fails.
    /// https://learn.microsoft.com/ef/core/saving/concurrency#application-managed-concurrency-tokens
    /// </summary>
    [ConcurrencyCheck]
    public Guid Version { get; private set; } = Guid.NewGuid();

    /// <summary>Records a new connection. Provider-supplied names are cut to fit their columns rather than failing the save.</summary>
    public void Connect(
        string externalTenantId, string? externalTenantName, string connectedAccountId, string? connectedAccountName,
        Guid connectedByMembershipId, DateTimeOffset connectedAt, string? scopes, string protectedCredentials)
    {
        Status = IntegrationStatus.Active;
        ExternalTenantId = externalTenantId;
        ExternalTenantName = Fit(externalTenantName);
        ConnectedAccountId = connectedAccountId;
        ConnectedAccountName = Fit(connectedAccountName);
        ConnectedByMembershipId = connectedByMembershipId;
        ConnectedAt = connectedAt;
        Scopes = Fit(scopes);
        ReplaceCredentials(protectedCredentials);
    }

    /// <summary>Forgets the credentials and who connected; the row stays so a reconnect reuses it.</summary>
    public void Disconnect()
    {
        Status = IntegrationStatus.Disconnected;
        ExternalTenantId = null;
        ExternalTenantName = null;
        ConnectedAccountId = null;
        ConnectedAccountName = null;
        ConnectedByMembershipId = null;
        ConnectedAt = null;
        Scopes = null;
        ReplaceCredentials(null);
    }

    /// <summary>Every state change goes through here, so every write changes <see cref="Version"/>.</summary>
    private void ReplaceCredentials(string? protectedCredentials)
    {
        ProtectedCredentials = protectedCredentials;
        Version = Guid.NewGuid();
    }

    private static string? Fit(string? value) => value is { Length: > NameMaxLength } ? value[..NameMaxLength] : value;
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

    [MaxLength(ChurchIntegration.ExternalIdMaxLength)]
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
