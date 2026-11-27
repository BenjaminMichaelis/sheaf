using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

public interface IGroupService
{
    Task<PagedResponse<GroupTypeResponse>> GetTypesAsync(PageRequest page, CancellationToken ct);
    Task<GroupTypeResponse> GetTypeAsync(Guid id, CancellationToken ct);
    Task<GroupTypeResponse> CreateTypeAsync(GroupTypeRequest request, CancellationToken ct);
    Task<GroupTypeResponse> UpdateTypeAsync(Guid id, GroupTypeRequest request, CancellationToken ct);
    Task<PagedResponse<GroupResponse>> GetGroupsAsync(PageRequest page, CancellationToken ct);
    Task<GroupResponse> GetGroupAsync(Guid id, CancellationToken ct);
    Task<GroupResponse> CreateGroupAsync(GroupRequest request, CancellationToken ct);
    Task<GroupResponse> UpdateGroupAsync(Guid id, Guid expectedVersion, GroupRequest request, CancellationToken ct);
    Task<GroupResponse> EndGroupAsync(Guid id, Guid expectedVersion, EndRequest request, CancellationToken ct);
}

public sealed class GroupService(ApplicationDbContext db, TrackerScope scope) : IGroupService
{
    private static GroupResponse Group(Context g) => new(g.Id, g.Name, g.StartsAt, g.EndsAt, g.GroupTypeId, g.Version);

    public async Task<PagedResponse<GroupTypeResponse>> GetTypesAsync(PageRequest page, CancellationToken ct)
    {
        return await db.GroupTypes.OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Select(t => new GroupTypeResponse(t.Id, t.Name, t.IsRetired)).PageAsync(page, ct);
    }

    public async Task<GroupTypeResponse> GetTypeAsync(Guid id, CancellationToken ct)
    {
        return await db.GroupTypes.Where(t => t.Id == id).Select(t => new GroupTypeResponse(t.Id, t.Name, t.IsRetired))
            .SingleOrDefaultAsync(ct) ?? throw Missing();
    }

    public async Task<GroupTypeResponse> CreateTypeAsync(GroupTypeRequest request, CancellationToken ct)
    {
        var type = new GroupType { Name = RequiredName(request.Name), IsRetired = request.IsRetired ?? false };
        db.GroupTypes.Add(type);
        await scope.SaveAsync(ct);
        return new(type.Id, type.Name, type.IsRetired);
    }

    public async Task<GroupTypeResponse> UpdateTypeAsync(Guid id, GroupTypeRequest request, CancellationToken ct)
    {
        var type = await db.GroupTypes.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw Missing();
        type.Name = RequiredName(request.Name);
        // An omitted IsRetired keeps the current value, so renaming never brings a retired type back by accident.
        type.IsRetired = request.IsRetired ?? type.IsRetired;
        await scope.SaveAsync(ct);
        return new(type.Id, type.Name, type.IsRetired);
    }

    public async Task<PagedResponse<GroupResponse>> GetGroupsAsync(PageRequest page, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        return await scope.Groups(actor).OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Select(g => new GroupResponse(g.Id, g.Name, g.StartsAt, g.EndsAt, g.GroupTypeId, g.Version)).PageAsync(page, ct);
    }

    public async Task<GroupResponse> GetGroupAsync(Guid id, CancellationToken ct) => Group((await scope.GroupAsync(id, ct)).Group);

    private async Task SetGroupAsync(Context group, GroupRequest request, CancellationToken ct)
    {
        group.Name = RequiredName(request.Name);
        if (request.StartsAt == default)
            throw Invalid();
        // A type from another church is filtered out by tenancy, so it reads as not found rather than revealing it exists.
        if (request.GroupTypeId is { } typeId)
        {
            var retired = await db.GroupTypes.Where(t => t.Id == typeId).Select(t => (bool?)t.IsRetired).SingleOrDefaultAsync(ct)
                ?? throw Missing();
            // A group keeps a type retired after it was given; only giving a retired type to a group is refused.
            if (retired && group.GroupTypeId != typeId)
                throw Invalid();
        }
        group.StartsAt = request.StartsAt;
        group.GroupTypeId = request.GroupTypeId;
    }

    public async Task<GroupResponse> CreateGroupAsync(GroupRequest request, CancellationToken ct)
    {
        var group = new Context { Name = RequiredName(request.Name) };
        await SetGroupAsync(group, request, ct);
        db.Contexts.Add(group);
        await scope.SaveAsync(ct);
        return Group(group);
    }

    public async Task<GroupResponse> UpdateGroupAsync(Guid id, Guid expectedVersion, GroupRequest request, CancellationToken ct)
    {
        var (_, group) = await scope.GroupAsync(id, ct);
        RequireVersion(group.Version, expectedVersion);
        if (group.EndsAt != null)
            throw Conflict();
        if (await db.ContextMembers.AnyAsync(m => m.ContextId == id && m.JoinedAt < request.StartsAt, ct)
            || await db.ContextLeaders.AnyAsync(l => l.ContextId == id && l.StartsAt < request.StartsAt, ct))
            throw Invalid();
        await SetGroupAsync(group, request, ct);
        group.Version = Guid.NewGuid();
        await scope.SaveAsync(ct, TrackerFailure.PreconditionFailed);
        return Group(group);
    }

    public async Task<GroupResponse> EndGroupAsync(Guid id, Guid expectedVersion, EndRequest request, CancellationToken ct)
    {
        var (_, group) = await scope.GroupAsync(id, ct);
        RequireVersion(group.Version, expectedVersion);
        if (group.EndsAt != null)
            throw Conflict();
        if (request.EndsAt < group.StartsAt
            || await db.ContextMembers.AnyAsync(m => m.ContextId == id && (m.JoinedAt > request.EndsAt || m.LeftAt > request.EndsAt), ct)
            || await db.ContextLeaders.AnyAsync(l => l.ContextId == id && (l.StartsAt > request.EndsAt || l.EndsAt > request.EndsAt), ct))
            throw Invalid();
        group.EndsAt = request.EndsAt;
        group.Version = Guid.NewGuid();
        await scope.SaveAsync(ct, TrackerFailure.PreconditionFailed);
        return Group(group);
    }
}
