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
        // redirects to the frontend with the outcome. A GET, so it needs no church header; the challenge records the
        // membership starting it, and only that membership can finish it.
        church.MapGet("/connect", async Task<Results<ChallengeHttpResult, ProblemHttpResult>> (
                    PlanningCenterConnectionService service, CancellationToken ct) =>
                {
                    try
                    {
                        return TypedResults.Challenge(
                            await service.BeginConnectAsync(ct), [PlanningCenterAuthenticationDefaults.Scheme]);
                    }
                    catch (PlanningCenterException exception) when (exception.Failure == PlanningCenterFailure.NotConfigured)
                    {
                        return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                            title: "Planning Center is not configured for this Sheaf installation.");
                    }
                })
            .Produces(StatusCodes.Status302Found)
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
