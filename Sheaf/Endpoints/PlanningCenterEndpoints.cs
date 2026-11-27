using Microsoft.AspNetCore.Http.HttpResults;

using Sheaf.Authorization;
using Sheaf.Integrations.PlanningCenter;

namespace Sheaf.Endpoints;

public static class PlanningCenterEndpoints
{
    /// <summary>
    /// The default callback, registered with Planning Center as the redirect URI. Microsoft's OpenID Connect handler
    /// answers it (<see cref="PlanningCenterAuthenticationDefaults.Scheme"/>), so no endpoint is mapped here.
    /// </summary>
    public const string CallbackPath = "/api/integrations/planning-center/callback";

    public const string OutcomeQueryName = "planningCenter";

    public static void MapPlanningCenter(this IEndpointRouteBuilder app)
    {
        var church = app.MapChurchGroup()
            .MapGroup("/integrations/planning-center")
            .RequirePermission(Permission.ManageIntegrations)
            .WithTags("Planning Center");

        church.MapGet("", async Task<Ok<PlanningCenterStatusResponse>> (PlanningCenterConnectionService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetStatusAsync(ct)))
            .WithName("GetPlanningCenterStatus")
            .WithSummary("Whether Planning Center is configured and connected for the current church");

        // A top-level browser navigation: the handler redirects to Planning Center, which returns to the callback, which
        // redirects to the frontend with the outcome. The challenge records the membership starting it, and only that
        // membership can finish it. Being a GET, it carries no church header and skips the origin check, so it refuses
        // navigations another site started (Sec-Fetch-Site) and requires the church the page is showing.
        church.MapGet("/connect", async Task<Results<ChallengeHttpResult, ProblemHttpResult>> (
                    HttpContext http, Guid churchId, PlanningCenterConnectionService service, CancellationToken ct) =>
                {
                    if (http.Request.Headers["Sec-Fetch-Site"] == "cross-site")
                    {
                        return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
                            title: "Connecting Planning Center must start from Sheaf.");
                    }
                    try
                    {
                        return TypedResults.Challenge(
                            await service.BeginConnectAsync(churchId, ct), [PlanningCenterAuthenticationDefaults.Scheme]);
                    }
                    catch (PlanningCenterException exception) when (exception.Failure == PlanningCenterFailure.NotConfigured)
                    {
                        return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                            title: "Planning Center is not configured for this Sheaf installation.");
                    }
                    catch (PlanningCenterException exception) when (exception.Failure == PlanningCenterFailure.ChurchMismatch)
                    {
                        return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                            type: "tag:sheaf,2026:problems/church-header-conflict",
                            title: "Church selection does not match this request.",
                            detail: "Reload the page to use the active church, then retry.");
                    }
                })
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithName("ConnectPlanningCenter")
            .WithSummary("Navigate the browser here to connect the current church; it returns to the configured ReturnUrl with the outcome");

        church.MapDelete("", async Task<NoContent> (PlanningCenterConnectionService service, CancellationToken ct) =>
                {
                    await service.DisconnectAsync(ct);
                    return TypedResults.NoContent();
                })
            .WithName("DisconnectPlanningCenter")
            .WithSummary("Revoke Sheaf's Planning Center access for the current church and forget its tokens");
    }
}
