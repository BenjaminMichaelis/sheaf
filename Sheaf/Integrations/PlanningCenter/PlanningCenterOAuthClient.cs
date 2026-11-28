using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Sheaf.Integrations.PlanningCenter;

/// <summary>Planning Center refused an OAuth request. <see cref="Error"/> is the OAuth error code when one was returned.</summary>
public sealed class PlanningCenterOAuthException(string? error, int statusCode)
    : Exception($"Planning Center OAuth request failed with status {statusCode} ({error ?? "no error code"}).")
{
    public string? Error { get; } = error;

    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Revokes Planning Center tokens. The connect step runs in the OpenID Connect handler
/// (<see cref="PlanningCenterAuthenticationDefaults.Scheme"/>), which has no revoke, so this follows the handler's own
/// code redemption: the endpoint from the handler's discovery document, its client credentials and backchannel, and an
/// <see cref="OpenIdConnectMessage"/> posted as a form. Refreshing tokens arrives with the first feature that calls
/// Planning Center's API.
/// </summary>
public sealed partial class PlanningCenterOAuthClient(
    IOptionsMonitor<OpenIdConnectOptions> openIdConnect,
    IOptions<PlanningCenterOptions> options,
    ILogger<PlanningCenterOAuthClient> logger)
{
    public const string HttpClientName = "PlanningCenter.OAuth";

    /// <summary>
    /// Revokes a refresh token and its access token (RFC 7009) at the discovery document's <c>revocation_endpoint</c>.
    /// Planning Center reports success for already-revoked tokens.
    /// </summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        OpenIdConnectOptions handler = openIdConnect.Get(PlanningCenterAuthenticationDefaults.Scheme);
        IConfigurationManager<OpenIdConnectConfiguration> manager = handler.ConfigurationManager
            ?? throw new InvalidOperationException("The Planning Center OpenID Connect handler has no configuration manager.");
        OpenIdConnectConfiguration configuration = await manager.GetConfigurationAsync(ct);
        if (string.IsNullOrEmpty(configuration.RevocationEndpoint))
            throw new InvalidOperationException("Planning Center's discovery document has no revocation_endpoint.");

        OpenIdConnectMessage request = new() { ClientId = handler.ClientId, ClientSecret = handler.ClientSecret };
        request.SetParameter("token", refreshToken);
        request.SetParameter("token_type_hint", OpenIdConnectParameterNames.RefreshToken);
        using FormUrlEncodedContent form = new(request.Parameters);
        using HttpResponseMessage response = await handler.Backchannel.PostAsync(configuration.RevocationEndpoint, form, ct);
        // RFC 7009: success is the status code; the client ignores the body.
        if (!response.IsSuccessStatusCode)
            throw new PlanningCenterOAuthException(ReadError(await response.Content.ReadAsStringAsync(ct)), (int)response.StatusCode);
    }

    /// <summary>
    /// Best effort revoke: returns false instead of throwing for any failure other than cancellation, so an admin can
    /// always disconnect. The cause is logged; a refusal such as <c>invalid_client</c> points at Sheaf's client secret.
    /// An unrevoked refresh token still expires in Planning Center after 90 days.
    /// </summary>
    public async Task<bool> TryRevokeAsync(string refreshToken, CancellationToken ct)
    {
        if (!options.Value.IsConfigured)
            return false;
        try
        {
            await RevokeAsync(refreshToken, ct);
            return true;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            LogRevokeFailed(exception);
            return false;
        }
    }

    /// <summary>The OAuth error code, parsed like the handler parses token responses; null when the body is not JSON.</summary>
    private static string? ReadError(string body)
    {
        try
        {
            return new OpenIdConnectMessage(body).Error;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Revoking a Planning Center token failed; it will expire on its own.")]
    private partial void LogRevokeFailed(Exception exception);
}
