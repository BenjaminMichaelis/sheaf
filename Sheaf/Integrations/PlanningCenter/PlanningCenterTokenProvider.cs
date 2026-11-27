using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

namespace Sheaf.Integrations.PlanningCenter;

/// <summary>
/// Supplies a usable access token for the current church's connection, refreshing it when it is close to expiry.
/// Planning Center issues a new refresh token on every refresh, so refreshes are serialized in-process by a lock, and
/// a write that loses to another process (detected by the integration's concurrency token) is reconciled rather than
/// dropped: Sheaf never throws away the only live grant, and never keeps one nobody owns.
/// </summary>
public sealed partial class PlanningCenterTokenProvider(
    ApplicationDbContext db,
    PlanningCenterConnectionStore store,
    PlanningCenterOAuthClient oauth,
    IntegrationRefreshLocks locks,
    TimeProvider clock,
    ILogger<PlanningCenterTokenProvider> logger)
{
    /// <summary>Tokens closer than this to expiry are refreshed first, so a request never starts with a dying token.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    /// <exception cref="PlanningCenterException">The church is not connected, or must be reconnected.</exception>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        ChurchIntegration connection = await store.Connection.SingleOrDefaultAsync(ct)
            ?? throw new PlanningCenterException(PlanningCenterFailure.NotConnected);
        if (UsableAccessToken(connection) is { } current)
            return current;

        SemaphoreSlim gate = locks.For(connection.Id);
        await gate.WaitAsync(ct);
        try
        {
            await db.Entry(connection).ReloadAsync(ct);
            if (UsableAccessToken(connection) is { } refreshedMeanwhile)
                return refreshedMeanwhile;
            if (connection.Status != IntegrationStatus.Active)
                throw new PlanningCenterException(PlanningCenterFailure.NotConnected);
            if (store.Read(connection) is not { } stored)
            {
                // Active but unreadable, for example after a Data Protection key loss: only a reconnect can fix it.
                return await MarkNeedsReauthorizationAsync(connection, keepCredentials: true, ct);
            }

            // From here Planning Center may rotate the refresh token, so the caller's cancellation must not discard the
            // new one before it is saved. The HTTP client's own timeouts still bound the call.
            CancellationToken uncancelled = CancellationToken.None;
            PlanningCenterTokenSet tokenSet;
            try
            {
                tokenSet = await oauth.RefreshAsync(stored.RefreshToken, uncancelled);
            }
            catch (PlanningCenterOAuthException exception) when (exception.IsInvalidGrant)
            {
                return await HandleRejectedRefreshAsync(connection, stored.RefreshToken, uncancelled);
            }

            return await SaveRefreshedAsync(connection, PlanningCenterCredentials.From(tokenSet, clock.GetUtcNow()), uncancelled);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The most saves <see cref="SaveRefreshedAsync"/> attempts before giving up on keeping a new grant.</summary>
    private const int MaxSaveAttempts = 3;

    /// <summary>
    /// Saves freshly issued credentials, reconciling with whatever another writer saved in the meantime. The new grant
    /// always ends up stored or revoked, never dropped.
    /// </summary>
    private async Task<string> SaveRefreshedAsync(
        ChurchIntegration connection, PlanningCenterCredentials fresh, CancellationToken ct)
    {
        string? organizationId = connection.ExternalTenantId;
        string protectedFresh = store.Protect(fresh);
        connection.Refresh(protectedFresh);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return fresh.AccessToken;
            }
            catch (DbUpdateConcurrencyException)
            {
                await db.Entry(connection).ReloadAsync(ct);
            }

            if (connection.Status == IntegrationStatus.Active && store.Read(connection) is { } winner)
            {
                // Another refresh or a reconnect saved first; its tokens are the ones Sheaf keeps.
                if (winner.RefreshToken != fresh.RefreshToken)
                    await oauth.TryRevokeAsync(fresh.RefreshToken, ct);
                return winner.AccessToken;
            }

            if (attempt < MaxSaveAttempts
                && connection.Status == IntegrationStatus.NeedsReauthorization
                && connection.ExternalTenantId == organizationId)
            {
                // A concurrent refresh with the old token was rejected and gave up, but this refresh holds a live grant.
                connection.Refresh(protectedFresh);
                continue;
            }

            // Disconnected meanwhile, or still contended after every attempt: the new grant cannot be stored, so
            // release it rather than leave a live token that nobody holds.
            await oauth.TryRevokeAsync(fresh.RefreshToken, ct);
            throw new PlanningCenterException(PlanningCenterFailure.NotConnected);
        }
    }

    /// <summary>
    /// Planning Center rejected the refresh token. If another process refreshed in the meantime its token is fine. If
    /// the row moved on (disconnected, reconnected, or already given up), the rejection says nothing about what is
    /// stored now, so nothing is written. Only a rejection of the token still stored means an admin has to connect again.
    /// </summary>
    private async Task<string> HandleRejectedRefreshAsync(ChurchIntegration connection, string rejectedRefreshToken, CancellationToken ct)
    {
        await db.Entry(connection).ReloadAsync(ct);
        if (UsableAccessToken(connection) is { } winner)
            return winner;
        if (connection.Status != IntegrationStatus.Active || store.Read(connection)?.RefreshToken != rejectedRefreshToken)
            throw new PlanningCenterException(PlanningCenterFailure.NotConnected);
        return await MarkNeedsReauthorizationAsync(connection, keepCredentials: false, ct);
    }

    /// <param name="keepCredentials">See <see cref="ChurchIntegration.RequireReauthorization"/>.</param>
    private async Task<string> MarkNeedsReauthorizationAsync(ChurchIntegration connection, bool keepCredentials, CancellationToken ct)
    {
        connection.RequireReauthorization(keepCredentials);
        try
        {
            await db.SaveChangesAsync(ct);
            LogNeedsReauthorization(connection.ChurchId);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another writer got there first; whatever it saved stands.
            await db.Entry(connection).ReloadAsync(ct);
            if (UsableAccessToken(connection) is { } lateWinner)
                return lateWinner;
        }
        throw new PlanningCenterException(PlanningCenterFailure.NotConnected);
    }

    private string? UsableAccessToken(ChurchIntegration connection) =>
        connection.Status == IntegrationStatus.Active
        && store.Read(connection) is { } stored
        && stored.AccessTokenExpiresAt - RefreshMargin > clock.GetUtcNow()
            ? stored.AccessToken
            : null;

    [LoggerMessage(LogLevel.Warning, "Planning Center connection for church {ChurchId} needs an admin to reconnect.")]
    private partial void LogNeedsReauthorization(Guid churchId);

}
