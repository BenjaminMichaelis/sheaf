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
    PlanningCenterConnectionStore store,
    ICurrentChurchActor currentActor,
    PlanningCenterOAuthClient oauth,
    IOptions<PlanningCenterOptions> options,
    TimeProvider clock,
    ILogger<PlanningCenterConnectionService> logger)
{
    private const IntegrationProvider Provider = PlanningCenterConnectionStore.Provider;

    /// <summary>The most times <see cref="DisconnectAsync"/> re-reads and retries after another writer changed the row.</summary>
    private const int MaxDisconnectAttempts = 5;

    public async Task<PlanningCenterStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        ChurchIntegration? connection = await store.Connection.AsNoTracking().SingleOrDefaultAsync(ct);
        return connection is null
            ? new(options.Value.IsConfigured, IntegrationStatus.Disconnected, null, null, null)
            : new(options.Value.IsConfigured, connection.Status, connection.ExternalTenantName, connection.ConnectedAccountName, connection.ConnectedAt);
    }

    /// <summary>
    /// The properties for a challenge to <see cref="PlanningCenterAuthenticationDefaults.Scheme"/>. They record the
    /// membership starting the connection; the handler encrypts them into the OAuth state, and only that membership can
    /// finish it (checked by <see cref="PlanningCenterOpenIdConnectEvents"/> before the code is exchanged).
    /// </summary>
    /// <exception cref="PlanningCenterException">Sheaf has no Planning Center application configured.</exception>
    public async Task<AuthenticationProperties> BeginConnectAsync(CancellationToken ct)
    {
        if (!options.Value.IsConfigured)
            throw new PlanningCenterException(PlanningCenterFailure.NotConfigured);
        ChurchActor actor = await RequireActorAsync(ct);
        return PlanningCenterConnectionStart.ToProperties(new(actor.ChurchId, actor.MembershipId));
    }

    /// <summary>Links the current church to the organization in a grant the OpenID Connect handler has validated.</summary>
    public async Task<PlanningCenterConnectOutcome> CompleteConnectAsync(PlanningCenterGrant grant, CancellationToken requestAborted)
    {
        ChurchActor actor = await RequireActorAsync(requestAborted);
        // The grant is live in Planning Center already, so the browser going away must not drop it unsaved and unrevoked.
        CancellationToken ct = CancellationToken.None;

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

        ChurchIntegration? connection = await store.Connection.SingleOrDefaultAsync(ct);
        if (connection is null)
        {
            connection = new ChurchIntegration { Provider = Provider };
            db.ChurchIntegrations.Add(connection);
        }
        string? replacedRefreshToken = store.Read(connection)?.RefreshToken;

        DateTimeOffset now = clock.GetUtcNow();
        connection.Connect(
            grant.OrganizationId, grant.OrganizationName, grant.PersonId, grant.PersonName, actor.MembershipId, now,
            options.Value.Scopes,
            store.Protect(new PlanningCenterCredentials(grant.AccessToken, grant.AccessTokenExpiresAt, grant.RefreshToken, now)));

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
            await oauth.TryRevokeAsync(replacedRefreshToken, ct);
        LogConnected(actor.ChurchId, grant.OrganizationId);
        return PlanningCenterConnectOutcome.Connected;
    }

    /// <summary>
    /// Revokes Sheaf's access in Planning Center and forgets the tokens and who connected. Disconnecting twice is harmless.
    /// A failed revoke does not stop the disconnect: the token still expires in Planning Center, and the client logs it.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken requestAborted)
    {
        ChurchActor actor = await RequireActorAsync(requestAborted);
        // Once a revoke starts, finish forgetting the tokens even if the browser goes away; otherwise the row would stay
        // Active holding a revoked grant.
        CancellationToken ct = CancellationToken.None;
        for (int attempt = 1; ; attempt++)
        {
            ChurchIntegration? connection = await store.Connection.SingleOrDefaultAsync(ct);
            if (connection is null)
                return;
            db.IntegrationTenantLinks.RemoveRange(await db.IntegrationTenantLinks
                .Where(link => link.Provider == Provider && link.ChurchId == actor.ChurchId).ToListAsync(ct));

            if (store.Read(connection) is { } stored)
                await oauth.TryRevokeAsync(stored.RefreshToken, ct);
            connection.Disconnect();
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxDisconnectAttempts)
            {
                // A refresh or reconnect saved meanwhile (new tokens, or different links). Start over from what is stored
                // now, so whatever it saved is revoked and released too.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<ChurchActor> RequireActorAsync(CancellationToken ct) =>
        await currentActor.GetAsync(ct) ?? throw new UnauthorizedAccessException("A church actor is required.");

    /// <summary>
    /// Revokes a grant that was not saved, unless the church's stored row holds the same refresh token: Planning Center
    /// may return one grant to two concurrent authorizations, and the one that saved first keeps it.
    /// </summary>
    private async Task RevokeUnlessStoredAsync(string refreshToken, CancellationToken ct)
    {
        string? stored = await store.Connection.AsNoTracking()
            .Select(integration => integration.ProtectedCredentials).SingleOrDefaultAsync(ct);
        if (store.Read(stored)?.RefreshToken != refreshToken)
            await oauth.TryRevokeAsync(refreshToken, ct);
    }

    [LoggerMessage(LogLevel.Error, "Saving a Planning Center connection failed.")]
    private partial void LogSaveFailed(Exception exception);

    [LoggerMessage(LogLevel.Information, "Church {ChurchId} connected to Planning Center organization {OrganizationId}.")]
    private partial void LogConnected(Guid churchId, string organizationId);
}
