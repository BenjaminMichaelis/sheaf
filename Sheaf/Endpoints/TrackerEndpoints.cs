using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Sheaf.Authentication;
using Sheaf.Authorization;
using Sheaf.Services;

namespace Sheaf.Endpoints;

public static class TrackerEndpoints
{
    public static void MapTracker(this IEndpointRouteBuilder app)
    {
        var api = app.MapChurchGroup().RequirePermission(Permission.TrackerAccess).WithTags("Tracker")
            .AddEndpointFilter<TrackerEndpointFilter>();
        MapPeople(api);
        MapGroups(api);
        MapHouseholds(api);
        MapAssignments(api, "members", AssignmentKind.Member);
        MapAssignments(api, "leaders", AssignmentKind.Leader);
        MapSessions(api);
    }

    private static void MapSessions(RouteGroupBuilder api)
    {
        api.MapGet("/groups/{groupId:guid}/sessions", async Task<Ok<PagedResponse<SessionSummaryResponse>>> (Guid groupId, int? offset, int? limit, IAttendanceService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetSessionsAsync(groupId, PageRequest.From(offset, limit), ct)))
            .Tracker("GetGroupSessions", "List a group's meetings, newest first, with how many came");
        api.MapGet("/groups/{groupId:guid}/sessions/{sessionId:guid}", async Task<Ok<SessionResponse>> (Guid groupId, Guid sessionId, IAttendanceService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetSessionAsync(groupId, sessionId, ct)))
            .Tracker("GetGroupSession", "Get a meeting with who came and which roster members were absent");
        // Recording can create people and reports possible duplicates by name, so it shares lookup's per-account limit.
        api.MapPost("/groups/{groupId:guid}/sessions", async Task<Created<SessionResponse>> (Guid groupId, SessionRequest request, IAttendanceService s, CancellationToken ct) =>
            {
                var result = await s.CreateSessionAsync(groupId, request, ct);
                return TypedResults.Created($"/api/church/groups/{groupId}/sessions/{result.Id}", result);
            })
            .Tracker("RecordGroupSession", "Record a meeting and who came, creating new people in the same save")
            .RequireRateLimiting(RequestSecurityExtensions.LookupRateLimitPolicy)
            .Produces(StatusCodes.Status429TooManyRequests);
        api.MapPut("/groups/{groupId:guid}/sessions/{sessionId:guid}", async Task<Ok<SessionResponse>> (Guid groupId, Guid sessionId, [FromHeader(Name = IfMatchHeader)] string? ifMatch, AttendeesRequest request, IAttendanceService s, CancellationToken ct) =>
            TypedResults.Ok(await s.UpdateAttendeesAsync(groupId, sessionId, EntityTag.ParseIfMatch(ifMatch), request, ct)))
            .Tracker("UpdateGroupSessionAttendees", "Replace who came to a meeting and its description; people taken off the list are kept as history")
            .RequiresIfMatch()
            .RequireRateLimiting(RequestSecurityExtensions.LookupRateLimitPolicy)
            .Produces(StatusCodes.Status429TooManyRequests);
        api.MapGet("/attendance-records", async Task<Ok<PagedResponse<AttendanceRecordResponse>>> (Guid? personId, string? userId, int? offset, int? limit, IAttendanceService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetRecordsAsync(personId, userId, PageRequest.From(offset, limit), ct)))
            .Tracker("GetAttendanceRecords", "Staff review who marked whom present or took them off a meeting, newest first")
            .RequirePermission(Permission.PeopleManage);
    }

    private static void MapPeople(RouteGroupBuilder api)
    {
        api.MapGet("/people/lookup", async Task<Ok<PersonLookupResponse[]>> (string? name, IPeopleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.LookupAsync(name, ct)))
            .Tracker("LookupPeople", "Find up to 20 active people by name (2+ characters), returning only name, household label, and current group names")
            .RequireRateLimiting(RequestSecurityExtensions.LookupRateLimitPolicy)
            .Produces(StatusCodes.Status429TooManyRequests);
        api.MapGet("/people/{id:guid}", async Task<Ok<PersonResponse>> (Guid id, IPeopleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetPersonAsync(id, ct)))
            .Tracker("GetPerson", "Get a Person's basic profile with household members' first names");
        // A similar name returns the confirmation problem with "similarPeople"; resend with ?confirm=true to create anyway.
        api.MapPost("/people", async Task<Created<PersonResponse>> (PersonRequest request, IPeopleService s, CancellationToken ct, bool confirm = false) =>
            {
                var result = await s.CreatePersonAsync(request, confirm, ct);
                return TypedResults.Created($"/api/church/people/{result.Id}", result);
            })
            .Tracker("CreatePerson", "Staff create a Person without required contacts or household").RequirePermission(Permission.PeopleManage);
        api.MapPut("/people/{id:guid}", async Task<Ok<PersonResponse>> (Guid id, [FromHeader(Name = IfMatchHeader)] string? ifMatch, PersonRequest request, IPeopleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.UpdatePersonAsync(id, EntityTag.ParseIfMatch(ifMatch), request, ct)))
            .Tracker("UpdatePerson", "Update a scoped Person profile without changing account links").RequiresIfMatch();
    }

    private static void MapGroups(RouteGroupBuilder api)
    {
        api.MapGet("/group-types", async Task<Ok<PagedResponse<GroupTypeResponse>>> (int? offset, int? limit, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetTypesAsync(PageRequest.From(offset, limit), ct)))
            .Tracker("GetGroupTypes", "List church-local group types");
        api.MapGet("/group-types/{id:guid}", async Task<Ok<GroupTypeResponse>> (Guid id, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetTypeAsync(id, ct)))
            .Tracker("GetGroupType", "Get a church-local group type");
        api.MapPost("/group-types", async Task<Created<GroupTypeResponse>> (GroupTypeRequest request, IGroupService s, CancellationToken ct) =>
            {
                var result = await s.CreateTypeAsync(request, ct);
                return TypedResults.Created($"/api/church/group-types/{result.Id}", result);
            })
            .Tracker("CreateGroupType", "Staff create a group type").Manage();
        api.MapPut("/group-types/{id:guid}", async Task<Ok<GroupTypeResponse>> (Guid id, GroupTypeRequest request, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.UpdateTypeAsync(id, request, ct)))
            .Tracker("UpdateGroupType", "Staff rename or retire a group type; groups keep a retired type, but no group can be given it").Manage();
        api.MapGet("/groups", async Task<Ok<PagedResponse<GroupResponse>>> (int? offset, int? limit, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetGroupsAsync(PageRequest.From(offset, limit), ct)))
            .Tracker("GetGroups", "List accessible groups, including retained ended groups for Staff");
        api.MapGet("/groups/{id:guid}", async Task<Ok<GroupResponse>> (Guid id, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetGroupAsync(id, ct)))
            .Tracker("GetGroup", "Get an accessible group");
        api.MapPost("/groups", async Task<Created<GroupResponse>> (GroupRequest request, IGroupService s, CancellationToken ct) =>
            {
                var result = await s.CreateGroupAsync(request, ct);
                return TypedResults.Created($"/api/church/groups/{result.Id}", result);
            })
            .Tracker("CreateGroup", "Staff create a named group with a start instant and optional type").Manage();
        api.MapPut("/groups/{id:guid}", async Task<Ok<GroupResponse>> (Guid id, [FromHeader(Name = IfMatchHeader)] string? ifMatch, GroupRequest request, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.UpdateGroupAsync(id, EntityTag.ParseIfMatch(ifMatch), request, ct)))
            .Tracker("UpdateGroup", "Staff update a group before ending it").RequiresIfMatch().Manage();
        api.MapPost("/groups/{id:guid}/end", async Task<Ok<GroupResponse>> (Guid id, [FromHeader(Name = IfMatchHeader)] string? ifMatch, EndRequest request, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.EndGroupAsync(id, EntityTag.ParseIfMatch(ifMatch), request, ct)))
            .Tracker("EndGroup", "Staff end a group without deleting assignment history").RequiresIfMatch().Manage();
    }

    private static void MapHouseholds(RouteGroupBuilder api)
    {
        api.MapGet("/households/{id:guid}", async Task<Ok<HouseholdResponse>> (Guid id, IHouseholdService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetHouseholdAsync(id, ct)))
            .Tracker("GetHousehold", "Get an associated household without full Person profiles");
        api.MapPost("/households", async Task<Created<HouseholdResponse>> (HouseholdRequest request, IHouseholdService s, CancellationToken ct) =>
            {
                var result = await s.CreateHouseholdAsync(request, ct);
                return TypedResults.Created($"/api/church/households/{result.Id}", result);
            })
            .Tracker("CreateHousehold", "Create an associated household structure");
        api.MapPut("/households/{id:guid}", async Task<Ok<HouseholdResponse>> (Guid id, [FromHeader(Name = IfMatchHeader)] string? ifMatch, HouseholdRequest request, IHouseholdService s, CancellationToken ct) =>
            TypedResults.Ok(await s.UpdateHouseholdAsync(id, EntityTag.ParseIfMatch(ifMatch), request, ct)))
            .Tracker("UpdateHousehold", "Manage whole associated households without granting Person profile rights").RequiresIfMatch();
    }

    private static void MapAssignments(RouteGroupBuilder api, string segment, AssignmentKind kind)
    {
        // Operation names and summaries are kept identical to the published OpenAPI contract.
        var (plural, singular, noun) = kind == AssignmentKind.Leader
            ? ("GroupLeaders", "GroupLeader", "leadership")
            : ("GroupMembers", "GroupMember", "membership");
        var historyNoun = kind == AssignmentKind.Leader ? "leaders" : "members";
        var intervalNoun = kind == AssignmentKind.Leader ? "Person leadership" : "group membership";
        var addNoun = kind == AssignmentKind.Leader ? "Person leadership" : "Person membership";

        api.MapGet($"/groups/{{groupId:guid}}/{segment}", async Task<Ok<PagedResponse<AssignmentResponse>>> (Guid groupId, int? offset, int? limit, IAssignmentService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetAssignmentsAsync(groupId, kind, PageRequest.From(offset, limit), ct)))
            .Tracker($"Get{plural}", $"Get retained group {historyNoun} history");
        api.MapGet($"/groups/{{groupId:guid}}/{segment}/{{assignmentId:guid}}", async Task<Ok<AssignmentResponse>> (Guid groupId, Guid assignmentId, IAssignmentService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetAssignmentAsync(groupId, assignmentId, kind, ct)))
            .Tracker($"Get{singular}", $"Get a retained {intervalNoun} interval");
        api.MapPost($"/groups/{{groupId:guid}}/{segment}", async Task<Created<AssignmentResponse>> (Guid groupId, AssignmentRequest request, IAssignmentService s, CancellationToken ct) =>
            {
                var result = await s.AddAssignmentAsync(groupId, request, kind, ct);
                return TypedResults.Created($"/api/church/groups/{groupId}/{segment}/{result.Id}", result);
            })
            .Tracker($"Add{singular}", $"Add a dated {addNoun} assignment").ManageIf(kind == AssignmentKind.Leader);
        api.MapPost($"/groups/{{groupId:guid}}/{segment}/{{assignmentId:guid}}/end", async Task<Ok<AssignmentResponse>> (Guid groupId, Guid assignmentId, EndRequest request, IAssignmentService s, CancellationToken ct) =>
            TypedResults.Ok(await s.EndAssignmentAsync(groupId, assignmentId, request, kind, ct)))
            .Tracker($"End{singular}", $"End a dated {noun} assignment without deleting history").ManageIf(kind == AssignmentKind.Leader);
    }

    internal const string IfMatchHeader = "If-Match";

    /// <summary>Names, summarizes, and declares the ProblemDetails responses shared by every tracker endpoint.</summary>
    private static RouteHandlerBuilder Tracker(this RouteHandlerBuilder builder, string name, string summary) =>
        builder.WithName(name).WithSummary(summary)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    /// <summary>
    /// Restricts an operation to callers who manage the whole tracker. The route group already requires
    /// <see cref="Permission.TrackerAccess"/>; which rows a caller reaches is decided in <see cref="TrackerScope"/>.
    /// </summary>
    private static RouteHandlerBuilder Manage(this RouteHandlerBuilder builder) =>
        builder.RequirePermission(Permission.TrackerManage);

    private static RouteHandlerBuilder ManageIf(this RouteHandlerBuilder builder, bool required) =>
        required ? builder.Manage() : builder;

    /// <summary>Declares the responses of a write that must send the resource's Version in If-Match.</summary>
    private static RouteHandlerBuilder RequiresIfMatch(this RouteHandlerBuilder builder) =>
        builder.ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
}

/// <summary>Translates tracker failures to ProblemDetails and stamps an ETag on versioned responses.</summary>
internal sealed class TrackerEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            var result = await next(context);
            // A Results<…> union wraps the result that will actually be written.
            var written = result is INestedHttpResult nested ? nested.Result : result;
            if (written is IValueHttpResult { Value: IVersioned versioned })
                context.HttpContext.Response.Headers.ETag = EntityTag.Format(versioned.Version);
            return result;
        }
        catch (TrackerException exception)
        {
            var (status, title) = exception.Failure switch
            {
                TrackerFailure.NotFound => (StatusCodes.Status404NotFound, "Resource not found."),
                TrackerFailure.Forbidden => (StatusCodes.Status403Forbidden, "Access denied."),
                TrackerFailure.InvalidInput => (StatusCodes.Status400BadRequest, "Invalid tracker input."),
                TrackerFailure.Conflict => (StatusCodes.Status409Conflict, "The resource changed or the group has ended."),
                TrackerFailure.PreconditionFailed => (StatusCodes.Status412PreconditionFailed, "The resource changed since it was read. Reload it and retry."),
                TrackerFailure.PreconditionRequired => (StatusCodes.Status428PreconditionRequired, "Send the resource's version in an If-Match header."),
                TrackerFailure.ConfirmationRequired => (StatusCodes.Status409Conflict, "This looks like a duplicate. Resend with confirm to save anyway."),
                TrackerFailure.TimeZoneUnavailable => (StatusCodes.Status500InternalServerError, "The church's time zone isn't available on this server. An Admin can set it again with PUT /api/church/settings."),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
            };
            var type = exception.Failure == TrackerFailure.ConfirmationRequired ? TrackerException.ConfirmationRequiredType : null;
            return TypedResults.Problem(statusCode: status, title: title, type: type, extensions: exception.Extensions);
        }
    }
}
