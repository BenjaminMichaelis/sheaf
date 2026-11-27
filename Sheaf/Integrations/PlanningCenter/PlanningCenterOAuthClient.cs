using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

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
/// Refreshes and revokes Planning Center tokens; the connect step itself runs in the OpenID Connect handler
/// (<see cref="PlanningCenterAuthenticationDefaults.Scheme"/>), which has no refresh or revoke. Token requests are not
/// retried: a refresh rotates the refresh token, so a blind retry could burn the only valid credential.
/// </summary>
public sealed class PlanningCenterOAuthClient(IHttpClientFactory httpClients, IOptions<PlanningCenterOptions> options)
{
    public const string HttpClientName = "PlanningCenter.OAuth";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<PlanningCenterTokenSet> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var settings = RequireConfigured();
        return RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret
        }, ct);
    }

    /// <summary>Revokes a refresh token and its access token. Planning Center reports success for already-revoked tokens.</summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        var settings = RequireConfigured();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token",
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret
        });
        using HttpResponseMessage response = await CreateClient().PostAsync(new Uri("oauth/revoke", UriKind.Relative), form, ct);
        await ThrowIfFailedAsync(response, ct);
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

    private async Task<PlanningCenterTokenSet> RequestTokensAsync(Dictionary<string, string> fields, CancellationToken ct)
    {
        using FormUrlEncodedContent form = new(fields);
        using HttpResponseMessage response = await CreateClient().PostAsync(new Uri("oauth/token", UriKind.Relative), form, ct);
        await ThrowIfFailedAsync(response, ct);
        TokenResponse tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(Json, ct)
            ?? throw PlanningCenterOAuthException.InvalidResponse("empty token response");
        if (string.IsNullOrEmpty(tokens.AccessToken) || string.IsNullOrEmpty(tokens.RefreshToken) || tokens.ExpiresIn <= 0)
            throw PlanningCenterOAuthException.InvalidResponse("incomplete token response");
        return new(tokens.AccessToken, tokens.RefreshToken, TimeSpan.FromSeconds(tokens.ExpiresIn), tokens.Scope);
    }

    /// <summary>The base address and User-Agent are set where the named client is registered.</summary>
    private HttpClient CreateClient() => httpClients.CreateClient(HttpClientName);

    private (string ClientId, string ClientSecret) RequireConfigured()
    {
        PlanningCenterOptions settings = options.Value;
        if (!settings.IsConfigured)
            throw new PlanningCenterException(PlanningCenterFailure.NotConfigured);
        return (settings.ClientId, settings.ClientSecret);
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        ErrorResponse? body = null;
        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            try
            {
                body = await response.Content.ReadFromJsonAsync<ErrorResponse>(Json, ct);
            }
            catch (JsonException)
            {
                // An unreadable error body still fails with the status code.
            }
        }
        throw new PlanningCenterOAuthException(body?.Error, (int)response.StatusCode, errorDescription: body?.ErrorDescription);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);

    private sealed record ErrorResponse(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
