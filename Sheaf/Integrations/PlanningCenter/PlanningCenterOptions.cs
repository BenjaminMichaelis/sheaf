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
    /// The callback registered with Planning Center, ending in <c>/api/integrations/planning-center/callback</c>. It must be
    /// served on the same site as the frontend so the browser sends the Sheaf cookies with the callback.
    /// </summary>
    public Uri? RedirectUri { get; set; }

    /// <summary>Where the browser goes after the callback; Sheaf adds a <c>planningCenter</c> query value with the outcome.</summary>
    public string ReturnUrl { get; set; } = "/";

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
/// unconfigured, and outside Development and Testing a User-Agent without contact details may be refused by Planning
/// Center ("app name plus a URL or email", per its authentication docs).
/// </summary>
public sealed class PlanningCenterOptionsValidator(IHostEnvironment environment) : IValidateOptions<PlanningCenterOptions>
{
    public ValidateOptionsResult Validate(string? name, PlanningCenterOptions options)
    {
        if (!options.IsPartlyConfigured)
            return ValidateOptionsResult.Success;
        if (!options.IsConfigured)
        {
            return ValidateOptionsResult.Fail(
                "PlanningCenter: set ClientId, ClientSecret, and an absolute RedirectUri together, or none of them.");
        }
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && !HasContact(options.UserAgent))
        {
            return ValidateOptionsResult.Fail(
                "PlanningCenter:UserAgent must name the app and a contact URL or email, such as \"Sheaf (ops@example.org)\".");
        }
        return ValidateOptionsResult.Success;
    }

    private static bool HasContact(string? userAgent) =>
        userAgent is not null && (userAgent.Contains('@') || userAgent.Contains("://", StringComparison.Ordinal));
}
