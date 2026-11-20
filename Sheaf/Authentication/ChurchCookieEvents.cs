using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

using Sheaf.Core.Authentication;
using Sheaf.Data;

namespace Sheaf.Authentication;

internal sealed class ChurchCookieEvents(
    ITenantContextInitializer initializer,
    ChurchClaimsPrincipalFactory principalFactory) : CookieAuthenticationEvents
{
    public override Task CheckSlidingExpiration(CookieSlidingExpirationContext context)
    {
        if (IsChurchSwitch(context.HttpContext))
        {
            context.ShouldRenew = false;
        }

        return base.CheckSlidingExpiration(context);
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (!Synchronize(context.Principal))
        {
            await RejectAsync(context);
            return;
        }

        // Identity rebuilds the principal before HttpContext.User is published.
        principalFactory.RenewalPrincipal = context.Principal;
        try
        {
            await SecurityStampValidator.ValidatePrincipalAsync(context);
            if (!Synchronize(context.Principal))
            {
                await RejectAsync(context);
            }
            else if (IsChurchSwitch(context.HttpContext))
            {
                context.ShouldRenew = false;
            }
        }
        catch
        {
            InitializeTenantContext(null, null);
            throw;
        }
        finally
        {
            principalFactory.RenewalPrincipal = null;
        }
    }

    public override Task SigningOut(CookieSigningOutContext context)
    {
        InitializeTenantContext(null, null);
        return base.SigningOut(context);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            return base.RedirectToLogin(context);
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            return base.RedirectToAccessDenied(context);
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private bool Synchronize(ClaimsPrincipal? principal)
    {
        ChurchSelection? selection = null;
        bool valid = principal is null
            || (principal.Identity?.IsAuthenticated == true
                && principal.ReadChurchClaims(out selection) != ChurchClaimState.Invalid);
        InitializeTenantContext(valid ? selection?.ChurchId : null, valid ? selection?.MembershipId : null);
        return valid;
    }

    private void InitializeTenantContext(Guid? churchId, Guid? membershipId)
    {
        // Audited tenant selection: the cookie principal's validated church claims define the request church.
#pragma warning disable RS0030
        initializer.Initialize(churchId, membershipId);
#pragma warning restore RS0030
    }

    private static bool IsChurchSwitch(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<ChurchSwitchEndpointMetadata>() is not null;

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}

internal sealed class ChurchSwitchEndpointMetadata
{
}
