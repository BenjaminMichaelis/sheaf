using Microsoft.AspNetCore.Http.HttpResults;

using Sheaf.Authorization;
using Sheaf.Services;

namespace Sheaf.Endpoints;

public static class TrackerEndpoints
{
    public static void MapTracker(this IEndpointRouteBuilder app)
    {
        var api = app.MapChurchGroup().RequirePermission(Permission.TrackerAccess).WithTags("Tracker")
            .AddEndpointFilter<TrackerErrorFilter>();
        MapPeople(api);
        MapGroups(api);
        MapHouseholds(api);
        MapAssignments(api, "members", AssignmentKind.Member);
        MapAssignments(api, "leaders", AssignmentKind.Leader);
    }

    private static void MapPeople(RouteGroupBuilder api)
    {
        api.MapGet("/people/lookup", async Task<Ok<PersonLookupResponse[]>> (string? name, IPeopleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.LookupAsync(name, ct)))
            .Tracker("LookupPeople", "Look up active people with limited identity only");
        api.MapGet("/people/{id:guid}", async Task<Ok<PersonResponse>> (Guid id, IPeopleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetPersonAsync(id, ct)))
            .Tracker("GetPerson", "Get a scoped Person profile");
        api.MapPost("/people", async Task<Created<PersonResponse>> (PersonRequest request, IPeopleService s, CancellationToken ct) =>
            {
                var result = await s.CreatePersonAsync(request, ct);
                return TypedResults.Created($"/api/church/people/{result.Id}", result);
            })
            .Tracker("CreatePerson", "Staff create a Person without required contacts or household");
        api.MapPut("/people/{id:guid}", async Task<Ok<PersonResponse>> (Guid id, PersonRequest request, IPeopleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.UpdatePersonAsync(id, request, ct)))
            .Tracker("UpdatePerson", "Update a scoped Person profile without changing account links");
    }

    private static void MapGroups(RouteGroupBuilder api)
    {
        api.MapGet("/group-types", async Task<Ok<GroupTypeResponse[]>> (IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetTypesAsync(ct)))
            .Tracker("GetGroupTypes", "List church-local group types");
        api.MapGet("/group-types/{id:guid}", async Task<Ok<GroupTypeResponse>> (Guid id, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetTypeAsync(id, ct)))
            .Tracker("GetGroupType", "Get a church-local group type");
        api.MapPost("/group-types", async Task<Created<GroupTypeResponse>> (GroupTypeRequest request, IGroupService s, CancellationToken ct) =>
            {
                var result = await s.CreateTypeAsync(request, ct);
                return TypedResults.Created($"/api/church/group-types/{result.Id}", result);
            })
            .Tracker("CreateGroupType", "Staff create a group type");
        api.MapPut("/group-types/{id:guid}", async Task<Ok<GroupTypeResponse>> (Guid id, GroupTypeRequest request, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.UpdateTypeAsync(id, request, ct)))
            .Tracker("UpdateGroupType", "Staff rename a group type");
        api.MapGet("/groups", async Task<Ok<GroupResponse[]>> (IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetGroupsAsync(ct)))
            .Tracker("GetGroups", "List accessible groups, including retained ended groups for Staff");
        api.MapGet("/groups/{id:guid}", async Task<Ok<GroupResponse>> (Guid id, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetGroupAsync(id, ct)))
            .Tracker("GetGroup", "Get an accessible group");
        api.MapPost("/groups", async Task<Created<GroupResponse>> (GroupRequest request, IGroupService s, CancellationToken ct) =>
            {
                var result = await s.CreateGroupAsync(request, ct);
                return TypedResults.Created($"/api/church/groups/{result.Id}", result);
            })
            .Tracker("CreateGroup", "Staff create a named group with a start instant and optional type");
        api.MapPut("/groups/{id:guid}", async Task<Ok<GroupResponse>> (Guid id, GroupRequest request, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.UpdateGroupAsync(id, request, ct)))
            .Tracker("UpdateGroup", "Staff update a group before ending it");
        api.MapPost("/groups/{id:guid}/end", async Task<Ok<GroupResponse>> (Guid id, EndRequest request, IGroupService s, CancellationToken ct) =>
            TypedResults.Ok(await s.EndGroupAsync(id, request, ct)))
            .Tracker("EndGroup", "Staff end a group without deleting assignment history");
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
        api.MapPut("/households/{id:guid}", async Task<Ok<HouseholdResponse>> (Guid id, HouseholdRequest request, IHouseholdService s, CancellationToken ct) =>
            TypedResults.Ok(await s.UpdateHouseholdAsync(id, request, ct)))
            .Tracker("UpdateHousehold", "Manage whole associated households without granting Person profile rights");
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

        api.MapGet($"/groups/{{groupId:guid}}/{segment}", async Task<Ok<AssignmentResponse[]>> (Guid groupId, IAssignmentService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetAssignmentsAsync(groupId, kind, ct)))
            .Tracker($"Get{plural}", $"Get retained group {historyNoun} history");
        api.MapGet($"/groups/{{groupId:guid}}/{segment}/{{assignmentId:guid}}", async Task<Ok<AssignmentResponse>> (Guid groupId, Guid assignmentId, IAssignmentService s, CancellationToken ct) =>
            TypedResults.Ok(await s.GetAssignmentAsync(groupId, assignmentId, kind, ct)))
            .Tracker($"Get{singular}", $"Get a retained {intervalNoun} interval");
        api.MapPost($"/groups/{{groupId:guid}}/{segment}", async Task<Created<AssignmentResponse>> (Guid groupId, AssignmentRequest request, IAssignmentService s, CancellationToken ct) =>
            {
                var result = await s.AddAssignmentAsync(groupId, request, kind, ct);
                return TypedResults.Created($"/api/church/groups/{groupId}/{segment}/{result.Id}", result);
            })
            .Tracker($"Add{singular}", $"Add a dated {addNoun} assignment");
        api.MapPost($"/groups/{{groupId:guid}}/{segment}/{{assignmentId:guid}}/end", async Task<Ok<AssignmentResponse>> (Guid groupId, Guid assignmentId, EndRequest request, IAssignmentService s, CancellationToken ct) =>
            TypedResults.Ok(await s.EndAssignmentAsync(groupId, assignmentId, request, kind, ct)))
            .Tracker($"End{singular}", $"End a dated {noun} assignment without deleting history");
    }

    /// <summary>Names, summarizes, and declares the ProblemDetails responses shared by every tracker endpoint.</summary>
    private static RouteHandlerBuilder Tracker(this RouteHandlerBuilder builder, string name, string summary) =>
        builder.WithName(name).WithSummary(summary)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
}

internal sealed class TrackerErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (TrackerException exception)
        {
            var (status, title) = exception.Failure switch
            {
                TrackerFailure.NotFound => (StatusCodes.Status404NotFound, "Resource not found."),
                TrackerFailure.Forbidden => (StatusCodes.Status403Forbidden, "Access denied."),
                TrackerFailure.InvalidInput => (StatusCodes.Status400BadRequest, "Invalid tracker input."),
                TrackerFailure.Conflict => (StatusCodes.Status409Conflict, "The resource changed or the group has ended."),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
            };
            return TypedResults.Problem(statusCode: status, title: title);
        }
    }
}
