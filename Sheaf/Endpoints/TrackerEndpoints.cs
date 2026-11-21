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
        api.MapGet("/people/lookup", async Task<Ok<PersonLookupResponse[]>> (string? name, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.LookupAsync(name, ct));
        }).WithName("LookupPeople").WithSummary("Look up active people with limited identity only")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapGet("/people/{id:guid}", async Task<Ok<PersonResponse>> (Guid id, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.GetPersonAsync(id, ct));
        }).WithName("GetPerson").WithSummary("Get a scoped Person profile")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPost("/people", async Task<Created<PersonResponse>> (PersonRequest request, ITrackerService s, CancellationToken ct) =>
        {
            var result = await s.CreatePersonAsync(request, ct);
            return TypedResults.Created($"/api/church/people/{result.Id}", result);
        }).WithName("CreatePerson").WithSummary("Staff create a Person without required contacts or household")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPut("/people/{id:guid}", async Task<Ok<PersonResponse>> (Guid id, PersonRequest request, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.UpdatePersonAsync(id, request, ct));
        }).WithName("UpdatePerson").WithSummary("Update a scoped Person profile without changing account links")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapGet("/group-types", async Task<Ok<GroupTypeResponse[]>> (ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.GetTypesAsync(ct));
        }).WithName("GetGroupTypes").WithSummary("List church-local group types")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapGet("/group-types/{id:guid}", async Task<Ok<GroupTypeResponse>> (Guid id, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.GetTypeAsync(id, ct));
        }).WithName("GetGroupType").WithSummary("Get a church-local group type")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPost("/group-types", async Task<Created<GroupTypeResponse>> (GroupTypeRequest request, ITrackerService s, CancellationToken ct) =>
        {
            var result = await s.CreateTypeAsync(request, ct);
            return TypedResults.Created($"/api/church/group-types/{result.Id}", result);
        }).WithName("CreateGroupType").WithSummary("Staff create a group type")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPut("/group-types/{id:guid}", async Task<Ok<GroupTypeResponse>> (Guid id, GroupTypeRequest request, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.UpdateTypeAsync(id, request, ct));
        }).WithName("UpdateGroupType").WithSummary("Staff rename a group type")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapGet("/groups", async Task<Ok<GroupResponse[]>> (ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.GetGroupsAsync(ct));
        }).WithName("GetGroups").WithSummary("List accessible groups, including retained ended groups for Staff")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapGet("/groups/{id:guid}", async Task<Ok<GroupResponse>> (Guid id, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.GetGroupAsync(id, ct));
        }).WithName("GetGroup").WithSummary("Get an accessible group")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPost("/groups", async Task<Created<GroupResponse>> (GroupRequest request, ITrackerService s, CancellationToken ct) =>
        {
            var result = await s.CreateGroupAsync(request, ct);
            return TypedResults.Created($"/api/church/groups/{result.Id}", result);
        }).WithName("CreateGroup").WithSummary("Staff create a named group with a start instant and optional type")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPut("/groups/{id:guid}", async Task<Ok<GroupResponse>> (Guid id, GroupRequest request, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.UpdateGroupAsync(id, request, ct));
        }).WithName("UpdateGroup").WithSummary("Staff update a group before ending it")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPost("/groups/{id:guid}/end", async Task<Ok<GroupResponse>> (Guid id, EndRequest request, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.EndGroupAsync(id, request, ct));
        }).WithName("EndGroup").WithSummary("Staff end a group without deleting assignment history")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapGet("/households/{id:guid}", async Task<Ok<HouseholdResponse>> (Guid id, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.GetHouseholdAsync(id, ct));
        }).WithName("GetHousehold").WithSummary("Get an associated household without full Person profiles")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPost("/households", async Task<Created<HouseholdResponse>> (HouseholdRequest request, ITrackerService s, CancellationToken ct) =>
        {
            var result = await s.CreateHouseholdAsync(request, ct);
            return TypedResults.Created($"/api/church/households/{result.Id}", result);
        }).WithName("CreateHousehold").WithSummary("Create an associated household structure")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPut("/households/{id:guid}", async Task<Ok<HouseholdResponse>> (Guid id, HouseholdRequest request, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.UpdateHouseholdAsync(id, request, ct));
        }).WithName("UpdateHousehold").WithSummary("Manage whole associated households without granting Person profile rights")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapGet("/groups/{groupId:guid}/members", async Task<Ok<AssignmentResponse[]>> (Guid groupId, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.GetAssignmentsAsync(groupId, false, ct));
        }).WithName("GetGroupMembers").WithSummary("Get retained group members history")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapGet("/groups/{groupId:guid}/members/{assignmentId:guid}", async Task<Ok<AssignmentResponse>> (Guid groupId, Guid assignmentId, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.GetAssignmentAsync(groupId, assignmentId, false, ct));
        }).WithName("GetGroupMember").WithSummary("Get a retained group membership interval")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPost("/groups/{groupId:guid}/members", async Task<Created<AssignmentResponse>> (Guid groupId, AssignmentRequest request, ITrackerService s, CancellationToken ct) =>
        {
            var result = await s.AddAssignmentAsync(groupId, request, false, ct);
            return TypedResults.Created($"/api/church/groups/{groupId}/members/{result.Id}", result);
        }).WithName("AddGroupMember").WithSummary("Add a dated Person membership assignment")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPost("/groups/{groupId:guid}/members/{assignmentId:guid}/end", async Task<Ok<AssignmentResponse>> (Guid groupId, Guid assignmentId, EndRequest request, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.EndAssignmentAsync(groupId, assignmentId, request, false, ct));
        }).WithName("EndGroupMember").WithSummary("End a dated membership assignment without deleting history")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapGet("/groups/{groupId:guid}/leaders", async Task<Ok<AssignmentResponse[]>> (Guid groupId, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.GetAssignmentsAsync(groupId, true, ct));
        }).WithName("GetGroupLeaders").WithSummary("Get retained group leaders history")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapGet("/groups/{groupId:guid}/leaders/{assignmentId:guid}", async Task<Ok<AssignmentResponse>> (Guid groupId, Guid assignmentId, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.GetAssignmentAsync(groupId, assignmentId, true, ct));
        }).WithName("GetGroupLeader").WithSummary("Get a retained Person leadership interval")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPost("/groups/{groupId:guid}/leaders", async Task<Created<AssignmentResponse>> (Guid groupId, AssignmentRequest request, ITrackerService s, CancellationToken ct) =>
        {
            var result = await s.AddAssignmentAsync(groupId, request, true, ct);
            return TypedResults.Created($"/api/church/groups/{groupId}/leaders/{result.Id}", result);
        }).WithName("AddGroupLeader").WithSummary("Add a dated Person leadership assignment")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        api.MapPost("/groups/{groupId:guid}/leaders/{assignmentId:guid}/end", async Task<Ok<AssignmentResponse>> (Guid groupId, Guid assignmentId, EndRequest request, ITrackerService s, CancellationToken ct) =>
        {
            return TypedResults.Ok(await s.EndAssignmentAsync(groupId, assignmentId, request, true, ct));
        }).WithName("EndGroupLeader").WithSummary("End a dated leadership assignment without deleting history")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
    }
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
            return TypedResults.Problem(statusCode: exception.StatusCode, title: exception.Message);
        }
    }
}
