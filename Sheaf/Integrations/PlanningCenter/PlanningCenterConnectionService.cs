using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Sheaf.Authorization;
using Sheaf.Data;

namespace Sheaf.Integrations.PlanningCenter;

public sealed record PlanningCenterStatusResponse(
    bool Configured,
    IntegrationStatus Status,
    string? OrganizationName,
    string? ConnectedPersonName,
    DateTimeOffset? ConnectedAt);

/// <summary>
/// What a completed authorization gave Sheaf: the tokens, and the person and organization from the validated id token.
/// </summary>
public sealed record PlanningCenterGrant(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    string PersonId,
    string? PersonName,
    string OrganizationId,
    string? OrganizationName);

/// <summary>How a callback ended. The value is sent to the frontend in the <c>planningCenter</c> query parameter.</summary>
public enum PlanningCenterConnectOutcome
{
    Connected,
    /// <summary>The user declined on Planning Center's consent screen.</summary>
    Denied,
    /// <summary>The callback did not match a pending request from this user and church.</summary>
    InvalidRequest,
    /// <summary>The Planning Center organization is already connected to another church.</summary>
    OrganizationInUse,
    Failed
}

/// <summary>Connects and disconnects the current church. Every call runs as the signed-in church actor.</summary>
public sealed partial class PlanningCenterConnectionService(
    ApplicationDbContext db,
    ICurrentChurchActor currentActor,
    PlanningCenterOAuthClient oauth,
    IntegrationCredentialProtector credentials,
    IOptions<PlanningCenterOptions> options,
    TimeProvider clock,
    ILogger<PlanningCenterConnectionService> logger)
{
    internal const IntegrationProvider Provider = IntegrationProvider.PlanningCenter;

    public async Task<PlanningCenterStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        ChurchIntegration? connection = await FindConnection(db).AsNoTracking().SingleOrDefaultAsync(ct);
        return connection is null
            ? new(options.Value.IsConfigured, IntegrationStatus.Disconnected, null, null, null)
            : new(options.Value.IsConfigured, connection.Status, connection.ExternalTenantName, connection.ConnectedAccountName, connection.ConnectedAt);
    }

    /// <summary>
    /// The properties for a challenge to <see cref="PlanningCenterAuthenticationDefaults.Scheme"/>. They record the
    /// membership starting the connection; the handler encrypts them into the OAuth state.
    /// </summary>
    /// <param name="expectedChurchId">The church the page starting the connection shows; it must be the selected one.</param>
    public async Task<AuthenticationProperties> BeginConnectAsync(Guid expectedChurchId, CancellationToken ct)
    {
        if (!options.Value.IsConfigured)
            throw new PlanningCenterException(PlanningCenterFailure.NotConfigured);
        ChurchActor actor = await RequireActorAsync(ct);
        if (actor.ChurchId != expectedChurchId)
            throw new PlanningCenterException(PlanningCenterFailure.ChurchMismatch);
        AuthenticationProperties properties = new();
        properties.Items[PlanningCenterAuthenticationDefaults.ChurchIdKey] = actor.ChurchId.ToString("D");
        properties.Items[PlanningCenterAuthenticationDefaults.MembershipIdKey] = actor.MembershipId.ToString("D");
        return properties;
    }

    /// <summary>Links the church to the organization in a grant the OpenID Connect handler has validated.</summary>
    public async Task<PlanningCenterConnectOutcome> CompleteConnectAsync(
        Guid startedByChurchId, Guid startedByMembershipId, PlanningCenterGrant grant, CancellationToken requestAborted)
    {
        ChurchActor actor = await RequireActorAsync(requestAborted);
        // The grant is live in Planning Center already, so the browser going away must not drop it unsaved and unrevoked.
        CancellationToken ct = CancellationToken.None;
        // The connection must be finished by the membership that started it, so a callback link sent to someone else fails.
        if (startedByChurchId != actor.ChurchId || startedByMembershipId != actor.MembershipId)
        {
            await TryRevokeAsync(grant.RefreshToken, ct);
            return PlanningCenterConnectOutcome.InvalidRequest;
        }

        IntegrationTenantLink? claim = await db.IntegrationTenantLinks.FindAsync([Provider, grant.OrganizationId], ct);
        // The new token is deliberately not revoked when another church holds the organization. Planning Center does not
        // document whether authorizing again returns the grant that church already holds, so revoking could disconnect
        // it, and this church cannot read the other's tokens to compare. The token is never stored and expires unused.
        if (claim is not null && claim.ChurchId != actor.ChurchId)
            return PlanningCenterConnectOutcome.OrganizationInUse;
        // Reconnecting to a different organization releases the one this church held before.
        db.IntegrationTenantLinks.RemoveRange(await db.IntegrationTenantLinks
            .Where(link => link.Provider == Provider && link.ChurchId == actor.ChurchId
                && link.ExternalTenantId != grant.OrganizationId)
            .ToListAsync(ct));
        if (claim is null)
            db.IntegrationTenantLinks.Add(new() { Provider = Provider, ExternalTenantId = grant.OrganizationId, ChurchId = actor.ChurchId });

        ChurchIntegration? connection = await FindConnection(db).SingleOrDefaultAsync(ct);
        if (connection is null)
        {
            connection = new ChurchIntegration { Provider = Provider };
            db.ChurchIntegrations.Add(connection);
        }
        string? replacedRefreshToken = credentials
            .TryUnprotect<PlanningCenterCredentials>(Provider, connection.ProtectedCredentials)?.RefreshToken;

        DateTimeOffset now = clock.GetUtcNow();
        connection.Status = IntegrationStatus.Active;
        connection.ExternalTenantId = grant.OrganizationId;
        // Provider-supplied text is cut to fit its column rather than failing the save.
        connection.ExternalTenantName = Truncate(grant.OrganizationName, 200);
        connection.ConnectedAccountId = Truncate(grant.PersonId, 100);
        connection.ConnectedAccountName = Truncate(grant.PersonName, 200);
        connection.ConnectedByMembershipId = actor.MembershipId;
        connection.ConnectedAt = now;
        connection.Scopes = Truncate(options.Value.Scopes, 200);
        SetCredentials(connection,
            new PlanningCenterCredentials(grant.AccessToken, grant.AccessTokenExpiresAt, grant.RefreshToken, now), credentials);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another connect or disconnect for this church finished first; keep its result.
            await RevokeUnlessStoredAsync(grant.RefreshToken, ct);
            return PlanningCenterConnectOutcome.Failed;
        }
        catch (DbUpdateException exception)
        {
            // Only a link held by another church means the organization is in use: it was claimed between the check
            // above and this save. Anything else, such as two admins of this church connecting at once, is a failure.
            bool claimedElsewhere = await db.IntegrationTenantLinks.AsNoTracking().AnyAsync(link =>
                link.Provider == Provider && link.ExternalTenantId == grant.OrganizationId && link.ChurchId != actor.ChurchId, ct);
            if (claimedElsewhere)
                return PlanningCenterConnectOutcome.OrganizationInUse;
            await RevokeUnlessStoredAsync(grant.RefreshToken, ct);
            LogSaveFailed(exception);
            return PlanningCenterConnectOutcome.Failed;
        }

        // Planning Center may hand back the same grant on a repeat authorization; never revoke the one just stored.
        if (replacedRefreshToken is not null && replacedRefreshToken != grant.RefreshToken)
            await TryRevokeAsync(replacedRefreshToken, ct);
        LogConnected(actor.ChurchId, grant.OrganizationId);
        return PlanningCenterConnectOutcome.Connected;
    }

    /// <summary>Revokes Sheaf's access in Planning Center and forgets the tokens. Disconnecting twice is harmless.</summary>
    public async Task DisconnectAsync(CancellationToken requestAborted)
    {
        ChurchActor actor = await RequireActorAsync(requestAborted);
        ChurchIntegration? connection = await FindConnection(db).SingleOrDefaultAsync(requestAborted);
        if (connection is null)
            return;

        // Once a revoke starts, finish forgetting the tokens even if the browser goes away; otherwise the row would stay
        // Active holding a revoked grant.
        CancellationToken ct = CancellationToken.None;
        db.IntegrationTenantLinks.RemoveRange(await db.IntegrationTenantLinks
            .Where(link => link.Provider == Provider && link.ChurchId == actor.ChurchId).ToListAsync(ct));
        for (int attempt = 0; ; attempt++)
        {
            if (credentials.TryUnprotect<PlanningCenterCredentials>(Provider, connection.ProtectedCredentials) is { } stored)
                await TryRevokeAsync(stored.RefreshToken, ct);

            connection.Status = IntegrationStatus.Disconnected;
            connection.ExternalTenantId = null;
            SetCredentials(connection, null, credentials);
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt == 0)
            {
                // A token refresh saved new credentials meanwhile; reload so those are revoked too.
                await db.Entry(connection).ReloadAsync(ct);
            }
        }
    }

    /// <summary>The current church's Planning Center row; the tenant filter supplies the church.</summary>
    internal static IQueryable<ChurchIntegration> FindConnection(ApplicationDbContext db) =>
        db.ChurchIntegrations.Where(integration => integration.Provider == Provider);

    /// <summary>Stores new credentials, or clears them when null, and bumps the concurrency version.</summary>
    internal static void SetCredentials(
        ChurchIntegration connection, PlanningCenterCredentials? value, IntegrationCredentialProtector credentials)
    {
        connection.ProtectedCredentials = value is null ? null : credentials.Protect(Provider, value);
        connection.Version = Guid.NewGuid();
    }

    private async Task<ChurchActor> RequireActorAsync(CancellationToken ct) =>
        await currentActor.GetAsync(ct) ?? throw new UnauthorizedAccessException("A church actor is required.");

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: var length } && length > maxLength ? value[..maxLength] : value;

    /// <summary>
    /// Best effort: a failed revoke must not leave Sheaf holding a connection the admin asked to remove. The token
    /// still expires in Planning Center (90 days for refresh tokens), and the failure is logged.
    /// </summary>
    /// <summary>
    /// Revokes a grant that was not saved, unless the church's stored row holds the same refresh token: Planning Center
    /// may return one grant to two concurrent authorizations, and the one that saved first keeps it.
    /// </summary>
    private async Task RevokeUnlessStoredAsync(string refreshToken, CancellationToken ct)
    {
        string? stored = await FindConnection(db).AsNoTracking()
            .Select(integration => integration.ProtectedCredentials).SingleOrDefaultAsync(ct);
        if (credentials.TryUnprotect<PlanningCenterCredentials>(Provider, stored)?.RefreshToken != refreshToken)
            await TryRevokeAsync(refreshToken, ct);
    }

    private async Task TryRevokeAsync(string refreshToken, CancellationToken ct)
    {
        // The client logs a failure with its cause.
        await oauth.TryRevokeAsync(refreshToken, ct);
    }

    [LoggerMessage(LogLevel.Error, "Saving a Planning Center connection failed.")]
    private partial void LogSaveFailed(Exception exception);

    [LoggerMessage(LogLevel.Information, "Church {ChurchId} connected to Planning Center organization {OrganizationId}.")]
    private partial void LogConnected(Guid churchId, string organizationId);

}
