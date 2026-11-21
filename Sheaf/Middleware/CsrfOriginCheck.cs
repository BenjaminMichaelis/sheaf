using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Sheaf.Middleware;

internal sealed class CsrfOriginCheck(RequestDelegate next, AllowedOriginPolicy origins)
{
    public Task InvokeAsync(HttpContext context)
    {
        HttpRequest request = context.Request;
        if (!HttpMethods.IsPost(request.Method) && !HttpMethods.IsPut(request.Method)
            && !HttpMethods.IsPatch(request.Method) && !HttpMethods.IsDelete(request.Method))
        {
            return next(context);
        }

        bool allowed;
        if (request.Headers.TryGetValue(HeaderNames.Origin, out StringValues origin))
        {
            allowed = origin.Count == 1 && origins.IsAllowedForRequest(origin.ToString(), request);
        }
        else if (request.Headers.TryGetValue("Sec-Fetch-Site", out StringValues fetchSite))
        {
            allowed = fetchSite.Count == 1 && fetchSite[0] == "same-origin";
        }
        else
        {
            allowed = request.Headers.TryGetValue(HeaderNames.Referer, out StringValues referer)
                && referer.Count == 1
                && AllowedOriginPolicy.TryGetOrigin(referer.ToString(), out string refererOrigin, allowPath: true)
                && origins.IsAllowedForRequest(refererOrigin, request);
        }

        if (allowed)
        {
            return next(context);
        }

        return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
            title: "Request origin could not be verified.",
            type: "tag:sheaf,2026:problems/csrf-origin-denied").ExecuteAsync(context);
    }
}
