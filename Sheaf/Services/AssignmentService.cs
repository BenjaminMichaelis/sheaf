using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

/// <summary>Whether an assignment is a group membership or a group leadership interval.</summary>
public enum AssignmentKind
{
    Member,
    Leader
}

public interface IAssignmentService
{
    Task<PagedResponse<AssignmentResponse>> GetAssignmentsAsync(Guid groupId, AssignmentKind kind, PageRequest page, CancellationToken ct);
    Task<AssignmentResponse> GetAssignmentAsync(Guid groupId, Guid assignmentId, AssignmentKind kind, CancellationToken ct);
    Task<AssignmentResponse> AddAssignmentAsync(Guid groupId, AssignmentRequest request, AssignmentKind kind, CancellationToken ct);
    Task<AssignmentResponse> EndAssignmentAsync(Guid groupId, Guid assignmentId, EndRequest request, AssignmentKind kind, CancellationToken ct);
}

/// <summary>
/// Dated membership and leadership intervals. Members may be managed by the group's current Leaders;
/// leadership writes require <see cref="Sheaf.Authorization.Permission.TrackerManage"/>, declared on their endpoints.
/// Every write bumps the group's version so it serializes against ending the group.
/// </summary>
public sealed class AssignmentService(ApplicationDbContext db, TrackerScope scope) : IAssignmentService
{
    // Filters are applied to the entities before projecting, so EF can translate them.
    private IQueryable<AssignmentResponse> Query(Guid groupId, AssignmentKind kind, Guid? assignmentId = null) => kind == AssignmentKind.Leader
        ? db.ContextLeaders.Where(l => l.ContextId == groupId && (assignmentId == null || l.Id == assignmentId))
            .OrderBy(l => l.StartsAt).ThenBy(l => l.Id)
            .Select(l => new AssignmentResponse(l.Id, l.PersonId, l.StartsAt, l.EndsAt))
        : db.ContextMembers.Where(m => m.ContextId == groupId && (assignmentId == null || m.Id == assignmentId))
            .OrderBy(m => m.JoinedAt).ThenBy(m => m.Id)
            .Select(m => new AssignmentResponse(m.Id, m.PersonId, m.JoinedAt, m.LeftAt));

    public async Task<PagedResponse<AssignmentResponse>> GetAssignmentsAsync(Guid groupId, AssignmentKind kind, PageRequest page, CancellationToken ct)
    {
        await scope.GroupAsync(groupId, ct);
        return await Query(groupId, kind).PageAsync(page, ct);
    }

    public async Task<AssignmentResponse> GetAssignmentAsync(Guid groupId, Guid assignmentId, AssignmentKind kind, CancellationToken ct)
    {
        await scope.GroupAsync(groupId, ct);
        return await Query(groupId, kind, assignmentId).SingleOrDefaultAsync(ct) ?? throw Missing();
    }

    public async Task<AssignmentResponse> AddAssignmentAsync(Guid groupId, AssignmentRequest request, AssignmentKind kind, CancellationToken ct)
    {
        var (actor, group) = await scope.GroupAsync(groupId, ct);
        if (group.EndsAt != null)
            throw Conflict();
        if (request.StartsAt < group.StartsAt || !await db.People.AnyAsync(p => p.Id == request.PersonId, ct))
            throw Invalid();
        // Assigning someone grants the actor access to their profile, so non-Staff may only assign people they can already see.
        if (!await scope.People(actor).AnyAsync(p => p.Id == request.PersonId, ct))
            throw Missing();
        group.Version = Guid.NewGuid();
        AssignmentResponse result;
        if (kind == AssignmentKind.Leader)
        {
            var leader = new ContextLeader { ContextId = groupId, PersonId = request.PersonId, StartsAt = request.StartsAt };
            db.ContextLeaders.Add(leader);
            result = new(leader.Id, leader.PersonId, leader.StartsAt, leader.EndsAt);
        }
        else
        {
            var member = new ContextMember { ContextId = groupId, PersonId = request.PersonId, JoinedAt = request.StartsAt };
            db.ContextMembers.Add(member);
            result = new(member.Id, member.PersonId, member.JoinedAt, member.LeftAt);
        }
        await scope.SaveAsync(ct);
        return result;
    }

    public async Task<AssignmentResponse> EndAssignmentAsync(Guid groupId, Guid assignmentId, EndRequest request, AssignmentKind kind, CancellationToken ct)
    {
        var (_, group) = await scope.GroupAsync(groupId, ct);
        if (group.EndsAt is { } end && request.EndsAt > end)
            throw Invalid();
        group.Version = Guid.NewGuid();
        AssignmentResponse result;
        if (kind == AssignmentKind.Leader)
        {
            var leader = await db.ContextLeaders.SingleOrDefaultAsync(l => l.Id == assignmentId && l.ContextId == groupId, ct) ?? throw Missing();
            if (leader.EndsAt != null || request.EndsAt < leader.StartsAt)
                throw Invalid();
            leader.EndsAt = request.EndsAt;
            result = new(leader.Id, leader.PersonId, leader.StartsAt, leader.EndsAt);
        }
        else
        {
            var member = await db.ContextMembers.SingleOrDefaultAsync(m => m.Id == assignmentId && m.ContextId == groupId, ct) ?? throw Missing();
            if (member.LeftAt != null || request.EndsAt < member.JoinedAt)
                throw Invalid();
            member.LeftAt = request.EndsAt;
            result = new(member.Id, member.PersonId, member.JoinedAt, member.LeftAt);
        }
        await scope.SaveAsync(ct);
        return result;
    }
}
