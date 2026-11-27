using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Sheaf.Authorization;
using Sheaf.Integrations.PlanningCenter;
using Sheaf.Middleware;

namespace Sheaf.Endpoints;

public static class PlanningCenterEndpoints
{
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

        // A top-level browser navigation: the handler redirects to Planning Center, which returns to the callback
        // (PlanningCenterAuthenticationDefaults.CallbackPath, answered by the handler), which redirects to the frontend
        // with the outcome. Being a navigation it has no church header or Origin, so filters require the selected
        // churchId and a navigation from Sheaf itself.
        // churchId is checked by ChurchQueryEndpointFilter; declaring it documents the query parameter in OpenAPI. It is
        // nullable so a missing value reaches the filter instead of failing binding first.
        church.MapGet("/connect", async Task<Results<ChallengeHttpResult, ProblemHttpResult>> (
                    Guid? churchId, PlanningCenterConnectionService service, IOptions<PlanningCenterOptions> options,
                    CancellationToken ct) =>
                options.Value.IsConfigured
                    ? TypedResults.Challenge(await service.BeginConnectAsync(ct), [PlanningCenterAuthenticationDefaults.Scheme])
                    : TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Planning Center is not configured for this Sheaf installation."))
            .AddEndpointFilter<NavigationOriginEndpointFilter>()
            .AddEndpointFilter<ChurchQueryEndpointFilter>()
            .Produces(StatusCodes.Status302Found)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithName("ConnectPlanningCenter")
            .WithSummary("Navigate the browser here to connect the current church; it returns to the configured ReturnUrl with the outcome");

        church.MapDelete("", async Task<Results<NoContent, ProblemHttpResult>> (PlanningCenterConnectionService service, CancellationToken ct) =>
                await service.DisconnectAsync(ct)
                    ? TypedResults.NoContent()
                    : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                        title: "The Planning Center connection changed meanwhile.",
                        detail: "Reload the page to see the current connection, then retry."))
            .WithName("DisconnectPlanningCenter")
            .WithSummary("Revoke Sheaf's Planning Center access for the current church and forget its tokens");
    }
}
