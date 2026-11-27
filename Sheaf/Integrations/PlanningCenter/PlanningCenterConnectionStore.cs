using Sheaf.Data;

namespace Sheaf.Integrations.PlanningCenter;

/// <summary>
/// The current church's Planning Center row and its encrypted credentials, shared by connecting, refreshing, and
/// disconnecting. The tenant filter supplies the church; state changes go through <see cref="ChurchIntegration"/>.
/// </summary>
public sealed class PlanningCenterConnectionStore(ApplicationDbContext db, IntegrationCredentialProtector protector)
{
    public const IntegrationProvider Provider = IntegrationProvider.PlanningCenter;

    /// <summary>The church's Planning Center row, at most one.</summary>
    public IQueryable<ChurchIntegration> Connection =>
        db.ChurchIntegrations.Where(integration => integration.Provider == Provider);

    /// <summary>The credentials, or null when there are none or they cannot be decrypted.</summary>
    public PlanningCenterCredentials? Read(ChurchIntegration connection) => Read(connection.ProtectedCredentials);

    public PlanningCenterCredentials? Read(string? protectedCredentials) =>
        protector.TryUnprotect<PlanningCenterCredentials>(Provider, protectedCredentials);

    public string Protect(PlanningCenterCredentials credentials) => protector.Protect(Provider, credentials);
}
