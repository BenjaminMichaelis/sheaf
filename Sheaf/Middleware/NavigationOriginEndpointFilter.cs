using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Sheaf.Middleware;

/// <summary>
/// For a GET that starts something on the user's behalf through a top-level navigation, which carries the Lax cookie and
/// no Origin header, so <see cref="CsrfOriginCheck"/> does not cover it. Allows a navigation the browser reports as
/// same-origin or user-initiated (<c>Sec-Fetch-Site: none</c>), or one whose <c>Referer</c> is an allowed origin (the
/// frontend, which may be another origin on the same site). Refuses anything else, such as a link on another site.
/// </summary>
internal sealed class NavigationOriginEndpointFilter(AllowedOriginPolicy origins) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpRequest request = context.HttpContext.Request;
        bool hasFetchSite = request.Headers.TryGetValue("Sec-Fetch-Site", out StringValues fetchSite);
        bool hasReferer = request.Headers.TryGetValue(HeaderNames.Referer, out StringValues referer);
        bool allowed =
            (hasFetchSite && fetchSite.Count == 1 && fetchSite[0] is "same-origin" or "none")
            || (hasReferer && referer.Count == 1
                && AllowedOriginPolicy.TryGetOrigin(referer.ToString(), out string refererOrigin, allowPath: true)
                && origins.IsAllowedForRequest(refererOrigin, request))
            // A client that sends neither header (not a current browser) is not one a cross-site page can drive.
            || (!hasFetchSite && !hasReferer);
        return allowed
            ? await next(context)
            : TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Request origin could not be verified.",
                type: "tag:sheaf,2026:problems/csrf-origin-denied");
    }
}
