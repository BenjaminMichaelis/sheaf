using Microsoft.AspNetCore.Http.HttpResults;

using Sheaf.Authentication;
using Sheaf.Authorization;
using Sheaf.Endpoints;
using Sheaf.Poc.People;
using Sheaf.Poc.Staff;

namespace Sheaf.Poc.Capture;

public static class CaptureEndpoints
{
    public static void MapPocCapture(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder api = app.MapChurchGroup().MapGroup("/poc").RequirePermission(Permission.TrackerAccess)
            .WithTags("POC").AddEndpointFilter<TrackerEndpointFilter>();

        api.MapPost("/captures/quick-add", async Task<Ok<CaptureResponse>> (QuickAddRequest request, CaptureService s, CancellationToken ct) =>
                TypedResults.Ok(await s.QuickAddAsync(request, ct)))
            .Poc("PocQuickAdd", "Add someone by hand; runs the duplicate check but no AI");
        // A note can call Claude, so it shares lookup's per-account rate limit.
        api.MapPost("/captures/note", async Task<Ok<CaptureResponse>> (NoteRequest request, CaptureService s, CancellationToken ct) =>
                TypedResults.Ok(await s.NoteAsync(request, ct)))
            .Poc("PocCaptureNote", "Redact a typed or dictated note, have AI extract people, and queue proposals for review")
            .RequireRateLimiting(RequestSecurityExtensions.LookupRateLimitPolicy)
            .Produces(StatusCodes.Status429TooManyRequests);
        api.MapGet("/captures", async Task<Ok<CaptureResponse[]>> (CaptureService s, CancellationToken ct) =>
                TypedResults.Ok(await s.RecentCapturesAsync(ct)))
            .Poc("PocRecentCaptures", "The signed-in leader's recent captures");

        api.MapGet("/proposals", async Task<Ok<ProposalResponse[]>> (bool? all, CaptureService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ProposalsAsync(all == true, ct)))
            .Poc("PocProposals", "Pending proposals: the caller's own, or every leader's for Staff with all=true");
        api.MapPost("/proposals/{id:guid}/decision", async Task<Ok<ProposalResponse>> (Guid id, ProposalDecisionRequest request, CaptureService s, CancellationToken ct) =>
                TypedResults.Ok(await s.DecideAsync(id, request, ct)))
            .Poc("PocDecideProposal", "Approve (optionally edited) or reject a proposal; writes an audit entry");

        api.MapGet("/inbox", async Task<Ok<SensitiveNoteResponse[]>> (CaptureService s, CancellationToken ct) =>
                TypedResults.Ok(await s.InboxAsync(ct)))
            .Poc("PocPastorInbox", "Staff read notes that were kept from AI because they mention something sensitive")
            .RequirePermission(Permission.PeopleManage);
        api.MapPost("/inbox/{id:guid}/reviewed", async Task<NoContent> (Guid id, CaptureService s, CancellationToken ct) =>
            {
                await s.MarkReviewedAsync(id, ct);
                return TypedResults.NoContent();
            })
            .Poc("PocMarkInboxReviewed", "Mark a pastor inbox note as reviewed")
            .RequirePermission(Permission.PeopleManage);

        api.MapGet("/people", async Task<Ok<MyPersonResponse[]>> (FollowUpService s, CancellationToken ct) =>
                TypedResults.Ok(await s.MyPeopleAsync(ct)))
            .Poc("PocMyPeople", "Members of the caller's groups and people they added, with the last touchpoint and a nudge, most urgent first");
        api.MapGet("/people/{id:guid}", async Task<Ok<PersonDetailResponse>> (Guid id, FollowUpService s, CancellationToken ct) =>
                TypedResults.Ok(await s.DetailAsync(id, ct)))
            .Poc("PocPersonDetail", "One person on the caller's list, with touchpoints, attendance and guardian");
        api.MapPost("/people/{id:guid}/touchpoints", async Task<Ok<TouchpointResponse>> (Guid id, TouchpointRequest request, FollowUpService s, CancellationToken ct) =>
                TypedResults.Ok(await s.LogTouchpointAsync(id, request, ct)))
            .Poc("PocLogTouchpoint", "Log a text, call, coffee or visit");
        api.MapPost("/people/{id:guid}/draft", async Task<Ok<DraftResponse>> (Guid id, DraftRequest request, FollowUpService s, CancellationToken ct) =>
                TypedResults.Ok(await s.DraftAsync(id, request, ct)))
            .Poc("PocDraftText", "AI drafts a text in the leader's voice; refused for minors and Don't contact")
            .RequireRateLimiting(RequestSecurityExtensions.LookupRateLimitPolicy)
            .Produces(StatusCodes.Status429TooManyRequests);
        api.MapPut("/people/{id:guid}/do-not-contact", async Task<Ok<MyPersonResponse>> (Guid id, DoNotContactRequest request, FollowUpService s, CancellationToken ct) =>
                TypedResults.Ok(await s.SetDoNotContactAsync(id, request, ct)))
            .Poc("PocSetDoNotContact", "Mark or clear Don't contact, which blocks drafts and nudges for every leader");
        api.MapPut("/people/{id:guid}/next-step", async Task<Ok<MyPersonResponse>> (Guid id, NextStepRequest request, FollowUpService s, CancellationToken ct) =>
                TypedResults.Ok(await s.SetNextStepAsync(id, request, ct)))
            .Poc("PocSetNextStep", "Set or clear the leader's planned next step");

        api.MapGet("/staff/overview", async Task<Ok<StaffOverviewResponse>> (string? leaderUserId, StaffOverviewService s, CancellationToken ct) =>
                TypedResults.Ok(await s.OverviewAsync(leaderUserId, ct)))
            .Poc("PocStaffOverview", "Funnel, follow-up gaps per leader, and group health; the funnel can be narrowed to one leader")
            .RequirePermission(Permission.PeopleManage);
        api.MapPost("/staff/brief", async Task<Ok<BriefResponse>> (StaffOverviewService s, CancellationToken ct) =>
                TypedResults.Ok(await s.BriefAsync(ct)))
            .Poc("PocWeeklyBrief", "AI writes the weekly brief from church-wide totals only")
            .RequirePermission(Permission.PeopleManage)
            .RequireRateLimiting(RequestSecurityExtensions.LookupRateLimitPolicy)
            .Produces(StatusCodes.Status429TooManyRequests);

        api.MapGet("/audit", async Task<Ok<AuditEntryResponse[]>> (CaptureService s, CancellationToken ct) =>
                TypedResults.Ok(await s.AuditLogAsync(ct)))
            .Poc("PocAuditLog", "The latest 100 audit entries: what AI proposed and what people approved")
            .RequirePermission(Permission.PeopleManage);
    }

    private static RouteHandlerBuilder Poc(this RouteHandlerBuilder builder, string name, string summary) =>
        builder.WithName(name).WithSummary(summary)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
}
