using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

public interface IAssignmentService
{
    Task<PagedResponse<AssignmentResponse>> GetAssignmentsAsync(Guid groupId, GroupRole role, PageRequest page, CancellationToken ct);
    Task<AssignmentResponse> GetAssignmentAsync(Guid groupId, Guid assignmentId, GroupRole role, CancellationToken ct);
    Task<AssignmentResponse> AddAssignmentAsync(Guid groupId, AssignmentRequest request, GroupRole role, CancellationToken ct);
    Task<AssignmentResponse> EndAssignmentAsync(Guid groupId, Guid assignmentId, EndRequest request, GroupRole role, CancellationToken ct);
    Task<AssignmentResponse> ChangeRoleAsync(Guid groupId, Guid assignmentId, RoleChangeRequest request, CancellationToken ct);
}

/// <summary>
/// Dated group assignments. A person has at most one assignment in a group at any instant, in one role; changing role
/// ends that assignment and starts another, so the history keeps when each role began and ended.
/// Members may be managed by anyone whose group role grants <see cref="GroupPermission.ManageMembers"/>; adding, ending,
/// or changing a Leader requires <see cref="Permission.TrackerManage"/>, declared on those endpoints.
/// Every write bumps the group's version so it serializes against ending the group.
/// </summary>
public sealed class AssignmentService(ApplicationDbContext db, TrackerScope scope) : IAssignmentService
{
    private static AssignmentResponse Response(GroupAssignment a) => new(a.Id, a.PersonId, a.Role, a.StartsAt, a.EndsAt);

    /// <summary>Leader writes are already limited to managers on their endpoints; Member writes need the group permission.</summary>
    private static GroupPermission WritePermission(GroupRole role) =>
        role == GroupRole.Member ? GroupPermission.ManageMembers : GroupPermission.View;

    public async Task<PagedResponse<AssignmentResponse>> GetAssignmentsAsync(Guid groupId, GroupRole role, PageRequest page, CancellationToken ct)
    {
        await scope.GroupAsync(groupId, GroupPermission.View, ct);
        return await db.GroupAssignments.Where(a => a.GroupId == groupId && a.Role == role)
            .OrderBy(a => a.StartsAt).ThenBy(a => a.Id)
            .Select(a => new AssignmentResponse(a.Id, a.PersonId, a.Role, a.StartsAt, a.EndsAt))
            .PageAsync(page, ct);
    }

    public async Task<AssignmentResponse> GetAssignmentAsync(Guid groupId, Guid assignmentId, GroupRole role, CancellationToken ct)
    {
        await scope.GroupAsync(groupId, GroupPermission.View, ct);
        return await db.GroupAssignments.Where(a => a.Id == assignmentId && a.GroupId == groupId && a.Role == role)
            .Select(a => new AssignmentResponse(a.Id, a.PersonId, a.Role, a.StartsAt, a.EndsAt))
            .SingleOrDefaultAsync(ct) ?? throw Missing();
    }

    public async Task<AssignmentResponse> AddAssignmentAsync(Guid groupId, AssignmentRequest request, GroupRole role, CancellationToken ct)
    {
        var (actor, group) = await scope.GroupAsync(groupId, WritePermission(role), ct);
        if (group.EndsAt != null)
            throw Conflict();
        if (request.StartsAt < group.StartsAt || !await db.People.AnyAsync(p => p.Id == request.PersonId, ct))
            throw Invalid();
        // Assigning someone grants the actor access to their profile, so non-Staff may only assign people they can already see.
        if (!await scope.People(actor).AnyAsync(p => p.Id == request.PersonId, ct))
            throw Missing();
        // Someone already in the group changes role instead of gaining a second assignment.
        if (await OverlapsAsync(groupId, request.PersonId, request.StartsAt, ct))
            throw Conflict();
        group.Version = Guid.NewGuid();
        var assignment = new GroupAssignment { GroupId = groupId, PersonId = request.PersonId, Role = role, StartsAt = request.StartsAt };
        db.GroupAssignments.Add(assignment);
        await scope.SaveAsync(ct);
        return Response(assignment);
    }

    public async Task<AssignmentResponse> EndAssignmentAsync(Guid groupId, Guid assignmentId, EndRequest request, GroupRole role, CancellationToken ct)
    {
        var (_, group) = await scope.GroupAsync(groupId, WritePermission(role), ct);
        if (group.EndsAt is { } end && request.EndsAt > end)
            throw Invalid();
        var assignment = await db.GroupAssignments
            .SingleOrDefaultAsync(a => a.Id == assignmentId && a.GroupId == groupId && a.Role == role, ct) ?? throw Missing();
        if (assignment.EndsAt != null || request.EndsAt < assignment.StartsAt)
            throw Invalid();
        group.Version = Guid.NewGuid();
        assignment.EndsAt = request.EndsAt;
        await scope.SaveAsync(ct);
        return Response(assignment);
    }

    public async Task<AssignmentResponse> ChangeRoleAsync(Guid groupId, Guid assignmentId, RoleChangeRequest request, CancellationToken ct)
    {
        var (_, group) = await scope.GroupAsync(groupId, GroupPermission.View, ct);
        if (group.EndsAt != null)
            throw Conflict();
        var current = await db.GroupAssignments
            .SingleOrDefaultAsync(a => a.Id == assignmentId && a.GroupId == groupId, ct) ?? throw Missing();
        if (!Enum.IsDefined(request.Role) || request.Role == current.Role
            || current.EndsAt != null || request.StartsAt < current.StartsAt)
            throw Invalid();
        group.Version = Guid.NewGuid();
        // A change from the moment the assignment began leaves no time in the old role worth keeping.
        if (request.StartsAt == current.StartsAt)
        {
            current.Role = request.Role;
            await scope.SaveAsync(ct);
            return Response(current);
        }
        current.EndsAt = request.StartsAt;
        var next = new GroupAssignment { GroupId = groupId, PersonId = current.PersonId, Role = request.Role, StartsAt = request.StartsAt };
        db.GroupAssignments.Add(next);
        await scope.SaveAsync(ct);
        return Response(next);
    }

    /// <summary>Whether the person has an assignment in the group that is still open at, or ends after, <paramref name="startsAt"/>.</summary>
    private Task<bool> OverlapsAsync(Guid groupId, Guid personId, DateTimeOffset startsAt, CancellationToken ct) =>
        db.GroupAssignments.AnyAsync(a => a.GroupId == groupId && a.PersonId == personId
            && (a.EndsAt == null || a.EndsAt > startsAt), ct);
}
