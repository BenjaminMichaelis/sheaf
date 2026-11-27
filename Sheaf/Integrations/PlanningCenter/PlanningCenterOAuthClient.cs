using System.Globalization;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Sheaf.Integrations.PlanningCenter;

/// <summary>Tokens from Planning Center's token endpoint. The refresh token replaces the previous one.</summary>
public sealed record PlanningCenterTokenSet(string AccessToken, string RefreshToken, TimeSpan ExpiresIn, string? Scope);

/// <summary>
/// Planning Center refused an OAuth request or answered with something unusable. <see cref="Error"/> is the OAuth error
/// code when one was returned.
/// </summary>
public sealed class PlanningCenterOAuthException(
    string? error, int statusCode, string? detail = null, string? errorDescription = null)
    : Exception($"Planning Center OAuth request failed with status {statusCode} ({error ?? "no error code"}){(detail is null ? "" : ": " + detail)}.")
{
    public string? Error { get; } = error;

    /// <summary>Planning Center's <c>error_description</c>. Not included in the message, which is logged.</summary>
    public string? ErrorDescription { get; } = errorDescription;

    public int StatusCode { get; } = statusCode;

    /// <summary>
    /// The code or refresh token is invalid, expired, or revoked; retrying will not help. Planning Center answers a dead
    /// refresh token with <c>invalid_grant</c> (sent with a 401), or with <c>invalid_request</c> and "The refresh token
    /// is no longer valid" (planningcenter/developers issues #284, #1310, #1318). Nothing else counts: an app that
    /// treated other failures, such as a firewall 403 page, as dead tokens lost every connection (#1310).
    /// </summary>
    public bool IsInvalidGrant =>
        Error == "invalid_grant"
        || (Error == "invalid_request"
            && ErrorDescription?.Contains("refresh token", StringComparison.OrdinalIgnoreCase) == true);

    public static PlanningCenterOAuthException InvalidResponse(string detail) => new("invalid_response", 200, detail);

    /// <summary>
    /// Whether <paramref name="exception"/> is a Planning Center or network failure rather than a bug or a cancelled
    /// request: an OAuth error, a transport error, unreadable JSON, or a resilience timeout or circuit break.
    /// </summary>
    public static bool IsRemoteFailure(Exception exception, CancellationToken ct) => exception switch
    {
        PlanningCenterOAuthException or HttpRequestException or JsonException or Polly.ExecutionRejectedException => true,
        OperationCanceledException => !ct.IsCancellationRequested,
        _ => false
    };
}

/// <summary>
/// Refreshes and revokes Planning Center tokens. The connect step runs in the OpenID Connect handler
/// (<see cref="PlanningCenterAuthenticationDefaults.Scheme"/>), which has no refresh or revoke, so this follows the
/// handler's own code redemption: endpoints from the handler's discovery document, its client credentials and
/// backchannel, an <see cref="OpenIdConnectMessage"/> posted as a form and parsed from the JSON response. Token
/// requests are not retried: a refresh rotates the refresh token, so a blind retry could burn the only valid credential.
/// </summary>
public sealed class PlanningCenterOAuthClient(
    IOptionsMonitor<OpenIdConnectOptions> openIdConnect, IOptions<PlanningCenterOptions> options)
{
    public const string HttpClientName = "PlanningCenter.OAuth";

    public async Task<PlanningCenterTokenSet> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        OpenIdConnectOptions handler = RequireConfigured();
        OpenIdConnectConfiguration configuration = await GetConfigurationAsync(handler, ct);
        OpenIdConnectMessage request = new()
        {
            GrantType = OpenIdConnectGrantTypes.RefreshToken,
            RefreshToken = refreshToken,
            ClientId = handler.ClientId,
            ClientSecret = handler.ClientSecret
        };
        OpenIdConnectMessage response = await PostAsync(handler, configuration.TokenEndpoint, request, ct);
        if (string.IsNullOrEmpty(response.AccessToken) || string.IsNullOrEmpty(response.RefreshToken)
            || !int.TryParse(response.ExpiresIn, NumberStyles.None, CultureInfo.InvariantCulture, out int expiresIn)
            || expiresIn <= 0)
        {
            throw PlanningCenterOAuthException.InvalidResponse("incomplete token response");
        }
        return new(response.AccessToken, response.RefreshToken, TimeSpan.FromSeconds(expiresIn), response.Scope);
    }

    /// <summary>
    /// Revokes a refresh token and its access token (RFC 7009) at the discovery document's <c>revocation_endpoint</c>.
    /// Planning Center reports success for already-revoked tokens.
    /// </summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        OpenIdConnectOptions handler = RequireConfigured();
        OpenIdConnectConfiguration configuration = await GetConfigurationAsync(handler, ct);
        if (string.IsNullOrEmpty(configuration.RevocationEndpoint))
            throw PlanningCenterOAuthException.InvalidResponse("discovery document has no revocation_endpoint");
        OpenIdConnectMessage request = new() { ClientId = handler.ClientId, ClientSecret = handler.ClientSecret };
        request.SetParameter("token", refreshToken);
        request.SetParameter("token_type_hint", OpenIdConnectParameterNames.RefreshToken);
        await PostAsync(handler, configuration.RevocationEndpoint, request, ct);
    }

    /// <summary>
    /// Best effort revoke: returns false instead of throwing when Planning Center is unreachable or Sheaf is no longer
    /// configured, so an admin can always disconnect. An unrevoked refresh token still expires after 90 days.
    /// </summary>
    public async Task<bool> TryRevokeAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            await RevokeAsync(refreshToken, ct);
            return true;
        }
        catch (Exception exception) when (PlanningCenterOAuthException.IsRemoteFailure(exception, ct)
            || exception is PlanningCenterException { Failure: PlanningCenterFailure.NotConfigured })
        {
            return false;
        }
    }

    private OpenIdConnectOptions RequireConfigured()
    {
        if (!options.Value.IsConfigured)
            throw new PlanningCenterException(PlanningCenterFailure.NotConfigured);
        return openIdConnect.Get(PlanningCenterAuthenticationDefaults.Scheme);
    }

    /// <summary>The handler's cached discovery document; a failure to fetch it is a remote failure.</summary>
    private static async Task<OpenIdConnectConfiguration> GetConfigurationAsync(OpenIdConnectOptions handler, CancellationToken ct)
    {
        IConfigurationManager<OpenIdConnectConfiguration> manager = handler.ConfigurationManager
            ?? throw new InvalidOperationException("The Planning Center OpenID Connect handler has no configuration manager.");
        try
        {
            return await manager.GetConfigurationAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new PlanningCenterOAuthException(null, 0, "discovery document unavailable: " + exception.Message);
        }
    }

    /// <summary>Posts like the handler's code redemption, then parses the body as an <see cref="OpenIdConnectMessage"/>.</summary>
    private static async Task<OpenIdConnectMessage> PostAsync(
        OpenIdConnectOptions handler, string endpoint, OpenIdConnectMessage request, CancellationToken ct)
    {
        using FormUrlEncodedContent form = new(request.Parameters);
        using HttpResponseMessage response = await handler.Backchannel.PostAsync(endpoint, form, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        OpenIdConnectMessage? message = null;
        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            try
            {
                message = new OpenIdConnectMessage(body);
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException)
            {
                // An unreadable body still fails with the status code.
            }
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new PlanningCenterOAuthException(
                message?.Error, (int)response.StatusCode, errorDescription: message?.ErrorDescription);
        }
        return message ?? throw PlanningCenterOAuthException.InvalidResponse("response was not JSON");
    }
}
