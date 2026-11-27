using Microsoft.Extensions.Primitives;

namespace Sheaf.Middleware;

/// <summary>
/// For a GET that starts something on the user's behalf through a top-level navigation, which carries the Lax cookie and
/// no Origin header, so <see cref="CsrfOriginCheck"/> does not cover it. Follows the Fetch Metadata resource isolation
/// policy (https://web.dev/articles/fetch-metadata): requests from browsers that send no <c>Sec-Fetch-Site</c>, and
/// same-origin, same-site, and user-initiated (<c>none</c>) requests are allowed; <c>cross-site</c> is refused. Unlike
/// that policy, cross-site top-level navigations are not exempted, because this GET starts an action.
/// </summary>
internal sealed class NavigationOriginEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Request.Headers.TryGetValue("Sec-Fetch-Site", out StringValues fetchSite)
        && fetchSite.Count == 1 && fetchSite[0] == "cross-site"
            ? TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Request origin could not be verified.",
                type: "tag:sheaf,2026:problems/csrf-origin-denied")
            : await next(context);
}
