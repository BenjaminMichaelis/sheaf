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

namespace Sheaf.Integrations.PlanningCenter;

public static class PlanningCenterAuthenticationDefaults
{
    /// <summary>The OpenID Connect scheme that runs the connect step. It never signs anyone in.</summary>
    public const string Scheme = "PlanningCenter";

    /// <summary>
    /// The default callback, registered with Planning Center as the redirect URI. The OpenID Connect handler answers it,
    /// so no endpoint is mapped for it.
    /// </summary>
    public const string CallbackPath = "/api/integrations/planning-center/callback";

    /// <summary>The query parameter that carries a <see cref="PlanningCenterConnectOutcome"/> back to the frontend.</summary>
    public const string OutcomeQueryName = "planningCenter";
}

/// <summary>The membership that started a connection. It travels in the OAuth state, which the handler encrypts.</summary>
internal sealed record PlanningCenterConnectionStart(Guid ChurchId, Guid MembershipId)
{
    private const string ChurchIdKey = "sheaf:churchId";
    private const string MembershipIdKey = "sheaf:membershipId";

    public static AuthenticationProperties ToProperties(PlanningCenterConnectionStart start)
    {
        AuthenticationProperties properties = new();
        properties.Items[ChurchIdKey] = start.ChurchId.ToString("D");
        properties.Items[MembershipIdKey] = start.MembershipId.ToString("D");
        return properties;
    }

    /// <summary>The start recorded in <paramref name="properties"/>, or null when there is none.</summary>
    public static PlanningCenterConnectionStart? From(AuthenticationProperties? properties) =>
        properties is not null
        && properties.Items.TryGetValue(ChurchIdKey, out string? churchId) && Guid.TryParse(churchId, out Guid church)
        && properties.Items.TryGetValue(MembershipIdKey, out string? membershipId) && Guid.TryParse(membershipId, out Guid membership)
            ? new(church, membership)
            : null;
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
        options.CallbackPath = planningCenter.RedirectUri?.AbsolutePath ?? PlanningCenterAuthenticationDefaults.CallbackPath;
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
/// token, and its nonce. The handler resolves this class from the request's services, so it takes scoped dependencies.
/// </summary>
internal sealed partial class PlanningCenterOpenIdConnectEvents(
    IOptions<PlanningCenterOptions> settings,
    IAuthorizationService authorization,
    ICurrentChurchActor currentActor,
    PlanningCenterConnectionService connections,
    PlanningCenterOAuthClient oauth,
    ILogger<PlanningCenterOpenIdConnectEvents> logger) : OpenIdConnectEvents
{
    private static readonly object ReachedCodeExchangeKey = new();

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
        // A failure before the code exchange (unreadable state, or no correlation cookie because another browser started
        // it) means this return does not belong to this browser. Planning Center reporting an error, or anything from
        // the exchange on, is a failed connection.
        LogRemoteFailure(context.Failure);
        bool reachedExchange = context.HttpContext.Items.ContainsKey(ReachedCodeExchangeKey)
            || context.Failure is OpenIdConnectProtocolException;
        Redirect(context.HttpContext, reachedExchange
            ? PlanningCenterConnectOutcome.Failed
            : PlanningCenterConnectOutcome.InvalidRequest);
        context.HandleResponse();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs after the handler has checked state and correlation and before it redeems the code, so a return that the
    /// signed-in admin did not start is refused without exchanging anything. This is the only place the starter is
    /// checked; everything after it runs as that admin.
    /// </summary>
    public override async Task AuthorizationCodeReceived(AuthorizationCodeReceivedContext context)
    {
        if (!await IsStartedByCurrentAdminAsync(context.HttpContext, PlanningCenterConnectionStart.From(context.Properties)))
        {
            Redirect(context.HttpContext, PlanningCenterConnectOutcome.InvalidRequest);
            context.HandleResponse();
            return;
        }
        context.HttpContext.Items[ReachedCodeExchangeKey] = true;
    }

    /// <remarks>
    /// The handler raises this after its own error handling, so an unexpected failure here (such as the database being
    /// unavailable) is caught here: the admin still returns to Sheaf, and the grant Planning Center just issued is
    /// revoked rather than left live and unstored.
    /// </remarks>
    public override async Task TicketReceived(TicketReceivedContext context)
    {
        PlanningCenterConnectOutcome outcome;
        try
        {
            outcome = await CompleteAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Unconditional: with the database failing, whether this grant is the one already stored cannot be checked.
            // Revoking a stored grant here (only possible if Planning Center returned the same grant to two concurrent
            // connects) is recovered by the next refresh marking the connection for reconnecting.
            bool revoked = await RevokeNewGrantAsync(context);
            LogCompleteFailed(exception, revoked);
            outcome = PlanningCenterConnectOutcome.Failed;
        }
        Redirect(context.HttpContext, outcome);
        context.HandleResponse();
    }

    /// <summary>
    /// The handler answers the callback before the authentication middleware reads the Sheaf cookie, so the admin
    /// finishing the connection is authenticated and authorized here, and must be the membership that started it.
    /// </summary>
    private async Task<bool> IsStartedByCurrentAdminAsync(HttpContext http, PlanningCenterConnectionStart? start)
    {
        if (start is null)
            return false;
        AuthenticateResult signedIn = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!signedIn.Succeeded || signedIn.Principal is null)
            return false;
        http.User = signedIn.Principal;
        AuthorizationResult allowed = await authorization.AuthorizeAsync(
            http.User, http, SheafPolicies.ForPermission(Permission.ManageIntegrations));
        return allowed.Succeeded
            && await currentActor.GetAsync(http.RequestAborted) is { } actor
            && actor.ChurchId == start.ChurchId
            && actor.MembershipId == start.MembershipId;
    }

    private async Task<PlanningCenterConnectOutcome> CompleteAsync(TicketReceivedContext context)
    {
        if (context.Principal is not { } principal || context.Properties is not { } properties)
            return PlanningCenterConnectOutcome.InvalidRequest;

        if (properties.GetTokenValue(OpenIdConnectParameterNames.AccessToken) is not { Length: > 0 } accessToken
            || properties.GetTokenValue(OpenIdConnectParameterNames.RefreshToken) is not { Length: > 0 } refreshToken
            || !DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset expiresAt)
            || principal.FindFirstValue("sub") is not { Length: > 0 } personId
            || principal.FindFirstValue("organization_id") is not { Length: > 0 } organizationId)
        {
            // Planning Center issued tokens Sheaf cannot use; release them rather than leave them live and unstored.
            await RevokeNewGrantAsync(context);
            return PlanningCenterConnectOutcome.Failed;
        }

        PlanningCenterGrant grant = new(
            accessToken, expiresAt, refreshToken,
            personId, principal.FindFirstValue("name"),
            organizationId, principal.FindFirstValue("organization_name"));
        return await connections.CompleteConnectAsync(grant, context.HttpContext.RequestAborted);
    }

    /// <summary>Revokes the refresh token this callback received, if any; the client logs a failure.</summary>
    private async Task<bool> RevokeNewGrantAsync(TicketReceivedContext context) =>
        context.Properties?.GetTokenValue(OpenIdConnectParameterNames.RefreshToken) is { Length: > 0 } refreshToken
        && await oauth.TryRevokeAsync(refreshToken, CancellationToken.None);

    private void Redirect(HttpContext http, PlanningCenterConnectOutcome outcome) =>
        http.Response.Redirect(QueryHelpers.AddQueryString(
            // Configured whenever a connection can start (validated on start); "/" only for a stray callback otherwise.
            settings.Value.ReturnUrl ?? "/", PlanningCenterAuthenticationDefaults.OutcomeQueryName, outcome.ToString()));

    [LoggerMessage(LogLevel.Warning, "Planning Center connection did not complete.")]
    private partial void LogRemoteFailure(Exception? exception);

    [LoggerMessage(LogLevel.Error, "Saving a Planning Center connection failed unexpectedly. New grant revoked: {Revoked}.")]
    private partial void LogCompleteFailed(Exception exception, bool revoked);
}
