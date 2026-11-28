using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;

using Sheaf.Data;

namespace Sheaf.Integrations.PlanningCenter;

/// <summary>
/// The current church's Planning Center row and its credentials, encrypted at rest with Data Protection. The tenant
/// filter supplies the church; state changes go through <see cref="ChurchIntegration"/>.
/// </summary>
public sealed class PlanningCenterConnectionStore(ApplicationDbContext db, IDataProtectionProvider dataProtection)
{
    public const IntegrationProvider Provider = IntegrationProvider.PlanningCenter;

    /// <summary>
    /// A versioned purpose with the provider as a sub-purpose, so one provider's ciphertext cannot be read back as
    /// another's. Changing these strings makes stored credentials unreadable.
    /// https://learn.microsoft.com/aspnet/core/security/data-protection/consumer-apis/purpose-strings
    /// </summary>
    private readonly IDataProtector _protector =
        dataProtection.CreateProtector("Sheaf.Integrations.Credentials.v1", nameof(IntegrationProvider.PlanningCenter));

    /// <summary>The church's Planning Center row, at most one.</summary>
    public IQueryable<ChurchIntegration> Connection =>
        db.ChurchIntegrations.Where(integration => integration.Provider == Provider);

    /// <summary>The credentials, or null when there are none or the current keys cannot decrypt them.</summary>
    public PlanningCenterCredentials? Read(ChurchIntegration connection) => Read(connection.ProtectedCredentials);

    public PlanningCenterCredentials? Read(string? protectedCredentials)
    {
        if (protectedCredentials is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<PlanningCenterCredentials>(_protector.Unprotect(protectedCredentials));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    public string Protect(PlanningCenterCredentials credentials) =>
        _protector.Protect(JsonSerializer.Serialize(credentials));
}
