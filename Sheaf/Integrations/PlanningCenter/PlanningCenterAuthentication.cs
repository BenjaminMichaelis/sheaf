using System.Globalization;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using Sheaf.Authorization;
using Sheaf.Endpoints;

namespace Sheaf.Integrations.PlanningCenter;

public static class PlanningCenterAuthenticationDefaults
{
    /// <summary>The OpenID Connect scheme that runs the connect step. It never signs anyone in.</summary>
    public const string Scheme = "PlanningCenter";

    internal const string ChurchIdKey = "sheaf:churchId";
    internal const string MembershipIdKey = "sheaf:membershipId";
}

/// <summary>
/// Configures Microsoft's OpenID Connect handler for Planning Center: authorization code with PKCE (the handler's
/// default), a query response so the return is a top-level GET that carries the Lax Sheaf cookie, and
/// <c>prompt=select_account</c> as Planning Center recommends. The id token is validated against Planning Center's
/// discovery document and signing keys, and supplies the organization.
/// </summary>
internal sealed class ConfigurePlanningCenterOpenIdConnect(
    IOptions<PlanningCenterOptions> settings, IHttpClientFactory httpClients, TimeProvider clock)
    : IConfigureNamedOptions<OpenIdConnectOptions>
{
    /// <summary>
    /// Stands in for a missing client id. The handler refuses options without one, and its scheme is registered whether
    /// or not Sheaf has a Planning Center app; the connect endpoint refuses to start while unconfigured.
    /// </summary>
    private const string UnconfiguredClientId = "unconfigured";

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name == PlanningCenterAuthenticationDefaults.Scheme)
            Configure(options);
    }

    public void Configure(OpenIdConnectOptions options)
    {
        PlanningCenterOptions planningCenter = settings.Value;
        options.Authority = planningCenter.BaseAddress.AbsoluteUri;
        options.ClientId = planningCenter.IsConfigured ? planningCenter.ClientId : UnconfiguredClientId;
        options.ClientSecret = planningCenter.ClientSecret;
        options.CallbackPath = planningCenter.RedirectUri?.AbsolutePath ?? PlanningCenterEndpoints.CallbackPath;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;
        options.Prompt = OpenIdConnectPrompt.SelectAccount;
        options.Scope.Clear();
        foreach (string scope in planningCenter.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            options.Scope.Add(scope);
        // The id token already carries the organization; Planning Center's claim names are used as sent.
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;
        // The tokens stay in the in-memory ticket for the events below; it is never written to a cookie.
        options.SaveTokens = true;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        // The named client adds the User-Agent Planning Center requires and never retries its POSTs.
        options.Backchannel = httpClients.CreateClient(PlanningCenterOAuthClient.HttpClientName);
        options.TimeProvider = clock;
        options.EventsType = typeof(PlanningCenterOpenIdConnectEvents);
    }
}

/// <summary>
/// Ends every return from Planning Center with a redirect to the frontend carrying the outcome, instead of signing in.
/// <see cref="TicketReceived"/> runs after the handler has validated state, correlation, the PKCE exchange, the id
/// token, and its nonce.
/// </summary>
internal sealed partial class PlanningCenterOpenIdConnectEvents(
    IOptions<PlanningCenterOptions> settings, ILogger<PlanningCenterOpenIdConnectEvents> logger) : OpenIdConnectEvents
{
    public override Task RedirectToIdentityProvider(RedirectContext context)
    {
        // The registered redirect URI, rather than one built from this request's host, so it matches exactly.
        if (settings.Value.RedirectUri is { IsAbsoluteUri: true } redirectUri)
            context.ProtocolMessage.RedirectUri = redirectUri.AbsoluteUri;
        return Task.CompletedTask;
    }

    public override Task AccessDenied(AccessDeniedContext context)
    {
        Redirect(context.HttpContext, PlanningCenterConnectOutcome.Denied);
        context.HandleResponse();
        return Task.CompletedTask;
    }

    public override Task RemoteFailure(RemoteFailureContext context)
    {
        // Without readable state the return was not started by this browser; anything later is a failed exchange.
        LogRemoteFailure(context.Failure);
        Redirect(context.HttpContext, context.Properties is null
            ? PlanningCenterConnectOutcome.InvalidRequest
            : PlanningCenterConnectOutcome.Failed);
        context.HandleResponse();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs after the handler has checked state and correlation and before it redeems the code, so a return that the
    /// signed-in admin did not start is refused without exchanging anything.
    /// </summary>
    public override async Task AuthorizationCodeReceived(AuthorizationCodeReceivedContext context)
    {
        if (!await IsStartedByCurrentAdminAsync(context.HttpContext, context.Properties))
        {
            Redirect(context.HttpContext, PlanningCenterConnectOutcome.InvalidRequest);
            context.HandleResponse();
        }
    }

    public override async Task TicketReceived(TicketReceivedContext context)
    {
        PlanningCenterConnectOutcome outcome = await CompleteAsync(context);
        Redirect(context.HttpContext, outcome);
        context.HandleResponse();
    }

    /// <summary>
    /// The handler answers the callback before the authentication middleware reads the Sheaf cookie, so the admin
    /// finishing the connection is authenticated and authorized here, and must be the membership that started it.
    /// </summary>
    private static async Task<bool> IsStartedByCurrentAdminAsync(HttpContext http, AuthenticationProperties? properties)
    {
        if (properties is null
            || !TryReadGuid(properties, PlanningCenterAuthenticationDefaults.ChurchIdKey, out Guid churchId)
            || !TryReadGuid(properties, PlanningCenterAuthenticationDefaults.MembershipIdKey, out Guid membershipId))
        {
            return false;
        }

        AuthenticateResult signedIn = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!signedIn.Succeeded || signedIn.Principal is null)
            return false;
        http.User = signedIn.Principal;
        AuthorizationResult allowed = await http.RequestServices.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(http.User, http, SheafPolicies.ForPermission(Permission.ManageIntegrations));
        return allowed.Succeeded
            && await http.RequestServices.GetRequiredService<ICurrentChurchActor>().GetAsync(http.RequestAborted) is { } actor
            && actor.ChurchId == churchId
            && actor.MembershipId == membershipId;
    }

    private static async Task<PlanningCenterConnectOutcome> CompleteAsync(TicketReceivedContext context)
    {
        HttpContext http = context.HttpContext;
        AuthenticationProperties? properties = context.Properties;
        if (context.Principal is not { } principal
            || properties is null
            || !TryReadGuid(properties, PlanningCenterAuthenticationDefaults.ChurchIdKey, out Guid churchId)
            || !TryReadGuid(properties, PlanningCenterAuthenticationDefaults.MembershipIdKey, out Guid membershipId))
        {
            return PlanningCenterConnectOutcome.InvalidRequest;
        }

        if (properties.GetTokenValue(OpenIdConnectParameterNames.AccessToken) is not { Length: > 0 } accessToken
            || properties.GetTokenValue(OpenIdConnectParameterNames.RefreshToken) is not { Length: > 0 } refreshToken
            || !DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset expiresAt)
            || principal.FindFirstValue("sub") is not { Length: > 0 } personId
            || principal.FindFirstValue("organization_id") is not { Length: > 0 } organizationId)
        {
            return PlanningCenterConnectOutcome.Failed;
        }

        PlanningCenterGrant grant = new(
            accessToken, expiresAt, refreshToken,
            personId, principal.FindFirstValue("name"),
            organizationId, principal.FindFirstValue("organization_name"));
        return await http.RequestServices.GetRequiredService<PlanningCenterConnectionService>()
            .CompleteConnectAsync(churchId, membershipId, grant, http.RequestAborted);
    }

    private static bool TryReadGuid(AuthenticationProperties properties, string key, out Guid value)
    {
        value = Guid.Empty;
        return properties.Items.TryGetValue(key, out string? text) && Guid.TryParse(text, out value);
    }

    private void Redirect(HttpContext http, PlanningCenterConnectOutcome outcome) =>
        http.Response.Redirect(QueryHelpers.AddQueryString(
            settings.Value.ReturnUrl, PlanningCenterEndpoints.OutcomeQueryName, outcome.ToString()));

    [LoggerMessage(LogLevel.Warning, "Planning Center connection did not complete.")]
    private partial void LogRemoteFailure(Exception? exception);
}
