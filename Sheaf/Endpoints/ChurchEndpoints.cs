using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;

using Sheaf.Authorization;
using Sheaf.Core.Authentication;

namespace Sheaf.Endpoints;

public static class ChurchEndpoints
{
    public static RouteGroupBuilder MapChurchGroup(this IEndpointRouteBuilder app) =>
        app.MapGroup("/api/church")
            .RequireAuthorization(SheafPolicies.ChurchMember)
            .AddEndpointFilter<ChurchHeaderEndpointFilter>();
}

internal sealed class ChurchHeaderEndpointFilter : IEndpointFilter
{
    private const string ChurchHeader = "X-Sheaf-Church";
    private const string ConflictTitle = "Church selection does not match this request.";
    private const string ConflictDetail = "Reload the page to use the active church, then retry.";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        StringValues values = http.Request.Headers[ChurchHeader];
        // Writes must name the church. Reads may omit it, but a read that names a church must match the selection,
        // so a stale tab cannot show one church's data under another (the web app sends it on every church request).
        bool safe = HttpMethods.IsGet(http.Request.Method)
            || HttpMethods.IsHead(http.Request.Method)
            || HttpMethods.IsOptions(http.Request.Method)
            || HttpMethods.IsTrace(http.Request.Method);
        if (safe && values.Count == 0)
        {
            return await next(context);
        }

        if (values.Count != 1
            || values[0] is not { Length: 36 } value
            || !Guid.TryParseExact(value, "D", out Guid headerChurchId)
            || headerChurchId == Guid.Empty
            || http.User.ReadChurchClaims(out ChurchSelection? selection) != ChurchClaimState.Selected
            || selection is null
            || selection.ChurchId != headerChurchId)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: "tag:sheaf,2026:problems/church-header-conflict",
                title: ConflictTitle,
                detail: ConflictDetail);
        }

        return await next(context);
    }
}
