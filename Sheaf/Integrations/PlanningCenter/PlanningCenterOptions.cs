using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Options;

namespace Sheaf.Integrations.PlanningCenter;

/// <summary>Settings for Sheaf's Planning Center OAuth application. Without them the integration reports itself unconfigured.</summary>
public sealed class PlanningCenterOptions
{
    public const string SectionName = "PlanningCenter";

    public Uri BaseAddress { get; set; } = new("https://api.planningcenteronline.com/");

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>
    /// The callback registered with Planning Center, ending in <c>/api/integrations/planning-center/callback</c>. The
    /// Sheaf cookie is host-only, so the callback must be on the host that set it: the API's host, or a frontend host
    /// that proxies <c>/api</c> to the API.
    /// </summary>
    public Uri? RedirectUri { get; set; }

    /// <summary>
    /// The frontend page the browser returns to after the callback; Sheaf adds a <c>planningCenter</c> query value with
    /// the outcome. Required once the integration is configured.
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary><c>openid</c> makes Planning Center issue the id token Sheaf reads the organization from.</summary>
    public string Scopes { get; set; } = "openid people";

    /// <summary>Planning Center requires a User-Agent that identifies the application.</summary>
    public string UserAgent { get; set; } = "Sheaf/1.0";

    [MemberNotNullWhen(true, nameof(ClientId), nameof(ClientSecret), nameof(RedirectUri))]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && RedirectUri is { IsAbsoluteUri: true };

    /// <summary>Whether any of the OAuth application settings is present.</summary>
    public bool IsPartlyConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) || !string.IsNullOrWhiteSpace(ClientSecret) || RedirectUri is not null;
}

/// <summary>
/// Fails startup on settings that would otherwise fail quietly: a half-configured OAuth application reports itself
/// unconfigured, a missing return URL strands the admin after connecting, a malformed User-Agent would only fail on the
/// first request, and outside Development a User-Agent without contact details may be refused by Planning Center ("app
/// name plus a URL or email", per its authentication docs).
/// </summary>
public sealed class PlanningCenterOptionsValidator(IHostEnvironment environment) : IValidateOptions<PlanningCenterOptions>
{
    public ValidateOptionsResult Validate(string? name, PlanningCenterOptions options)
    {
        // The HTTP client sets this header whether or not an OAuth application is configured, so it is checked first.
        if (!IsValidUserAgent(options.UserAgent))
        {
            return ValidateOptionsResult.Fail(
                "PlanningCenter:UserAgent is not a valid User-Agent header value, such as \"Sheaf (ops@example.org)\".");
        }
        if (!options.IsPartlyConfigured)
            return ValidateOptionsResult.Success;
        if (!options.IsConfigured)
        {
            return ValidateOptionsResult.Fail(
                "PlanningCenter: set ClientId, ClientSecret, and an absolute RedirectUri together, or none of them.");
        }
        // On Linux a path such as "/api/..." binds as an absolute file:// URI, so the scheme is checked, not just absoluteness.
        if (options.RedirectUri.Scheme != Uri.UriSchemeHttps
            && !(options.RedirectUri.Scheme == Uri.UriSchemeHttp && options.RedirectUri.IsLoopback))
        {
            return ValidateOptionsResult.Fail(
                "PlanningCenter:RedirectUri must be an https URL (http only for localhost), registered with Planning Center.");
        }
        if (string.IsNullOrWhiteSpace(options.ReturnUrl))
        {
            return ValidateOptionsResult.Fail(
                "PlanningCenter:ReturnUrl must name the frontend page to return to after connecting.");
        }
        if (!environment.IsDevelopment() && !HasContact(options.UserAgent))
        {
            return ValidateOptionsResult.Fail(
                "PlanningCenter:UserAgent must name the app and a contact URL or email, such as \"Sheaf (ops@example.org)\".");
        }
        return ValidateOptionsResult.Success;
    }

    /// <summary>Parses the value as the HTTP client will when it sets the header, so a bad value fails at start.</summary>
    private static bool IsValidUserAgent(string userAgent)
    {
        using HttpRequestMessage probe = new();
        return probe.Headers.UserAgent.TryParseAdd(userAgent);
    }

    private static bool HasContact(string? userAgent) =>
        userAgent is not null && (userAgent.Contains('@') || userAgent.Contains("://", StringComparison.Ordinal));
}
