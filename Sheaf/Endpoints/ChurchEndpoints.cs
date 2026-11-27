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

internal static class ChurchProblems
{
    /// <summary>The request named a church other than the one the session has selected, such as from a stale tab.</summary>
    public static ProblemHttpResult SelectionConflict() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        type: "tag:sheaf,2026:problems/church-header-conflict",
        title: "Church selection does not match this request.",
        detail: "Reload the page to use the active church, then retry.");

    public static bool IsSelected(HttpContext http, Guid churchId) =>
        churchId != Guid.Empty
        && http.User.ReadChurchClaims(out ChurchSelection? selection) == ChurchClaimState.Selected
        && selection is not null
        && selection.ChurchId == churchId;
}

/// <summary>
/// For a navigation (which cannot send the church header): requires a <c>churchId</c> query value naming the selected
/// church, so a stale tab cannot act on whichever church the session switched to.
/// </summary>
internal sealed class ChurchQueryEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        Guid.TryParseExact(context.HttpContext.Request.Query["churchId"], "D", out Guid churchId)
        && ChurchProblems.IsSelected(context.HttpContext, churchId)
            ? await next(context)
            : ChurchProblems.SelectionConflict();
}

internal sealed class ChurchHeaderEndpointFilter : IEndpointFilter
{
    private const string ChurchHeader = "X-Sheaf-Church";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method)
            || HttpMethods.IsHead(http.Request.Method)
            || HttpMethods.IsOptions(http.Request.Method)
            || HttpMethods.IsTrace(http.Request.Method))
        {
            return await next(context);
        }

        StringValues values = http.Request.Headers[ChurchHeader];
        return values.Count == 1
            && values[0] is { Length: 36 } value
            && Guid.TryParseExact(value, "D", out Guid headerChurchId)
            && ChurchProblems.IsSelected(http, headerChurchId)
                ? await next(context)
                : ChurchProblems.SelectionConflict();
    }
}
