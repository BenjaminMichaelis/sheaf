using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

public interface IGroupService
{
    Task<GroupTypeResponse[]> GetTypesAsync(CancellationToken ct);
    Task<GroupTypeResponse> GetTypeAsync(Guid id, CancellationToken ct);
    Task<GroupTypeResponse> CreateTypeAsync(GroupTypeRequest request, CancellationToken ct);
    Task<GroupTypeResponse> UpdateTypeAsync(Guid id, GroupTypeRequest request, CancellationToken ct);
    Task<GroupResponse[]> GetGroupsAsync(CancellationToken ct);
    Task<GroupResponse> GetGroupAsync(Guid id, CancellationToken ct);
    Task<GroupResponse> CreateGroupAsync(GroupRequest request, CancellationToken ct);
    Task<GroupResponse> UpdateGroupAsync(Guid id, GroupRequest request, CancellationToken ct);
    Task<GroupResponse> EndGroupAsync(Guid id, EndRequest request, CancellationToken ct);
}

public sealed class GroupService(ApplicationDbContext db, TrackerScope scope) : IGroupService
{
    private static GroupResponse Group(Context g) => new(g.Id, g.Name, g.StartsAt, g.EndsAt, g.GroupTypeId);

    public async Task<GroupTypeResponse[]> GetTypesAsync(CancellationToken ct)
    {
        await scope.ActorAsync(ct);
        return await db.GroupTypes.OrderBy(t => t.Name).ThenBy(t => t.Id).Take(200)
            .Select(t => new GroupTypeResponse(t.Id, t.Name)).ToArrayAsync(ct);
    }

    public async Task<GroupTypeResponse> GetTypeAsync(Guid id, CancellationToken ct)
    {
        await scope.ActorAsync(ct);
        return await db.GroupTypes.Where(t => t.Id == id).Select(t => new GroupTypeResponse(t.Id, t.Name))
            .SingleOrDefaultAsync(ct) ?? throw Missing();
    }

    public async Task<GroupTypeResponse> CreateTypeAsync(GroupTypeRequest request, CancellationToken ct)
    {
        await scope.StaffAsync(ct);
        var type = new GroupType { Name = RequiredName(request.Name) };
        db.GroupTypes.Add(type);
        await scope.SaveAsync(ct);
        return new(type.Id, type.Name);
    }

    public async Task<GroupTypeResponse> UpdateTypeAsync(Guid id, GroupTypeRequest request, CancellationToken ct)
    {
        await scope.StaffAsync(ct);
        var type = await db.GroupTypes.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw Missing();
        type.Name = RequiredName(request.Name);
        await scope.SaveAsync(ct);
        return new(type.Id, type.Name);
    }

    public async Task<GroupResponse[]> GetGroupsAsync(CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        return await scope.Groups(actor).OrderBy(g => g.Name).ThenBy(g => g.Id).Take(200)
            .Select(g => new GroupResponse(g.Id, g.Name, g.StartsAt, g.EndsAt, g.GroupTypeId)).ToArrayAsync(ct);
    }

    public async Task<GroupResponse> GetGroupAsync(Guid id, CancellationToken ct) => Group((await scope.GroupAsync(id, false, ct)).Group);

    private async Task SetGroupAsync(Context group, GroupRequest request, CancellationToken ct)
    {
        group.Name = RequiredName(request.Name);
        if (request.StartsAt == default)
            throw Invalid();
        // A type from another church is filtered out by tenancy, so it reads as not found rather than revealing it exists.
        if (request.GroupTypeId is { } typeId && !await db.GroupTypes.AnyAsync(t => t.Id == typeId, ct))
            throw Missing();
        group.StartsAt = request.StartsAt;
        group.GroupTypeId = request.GroupTypeId;
    }

    public async Task<GroupResponse> CreateGroupAsync(GroupRequest request, CancellationToken ct)
    {
        await scope.StaffAsync(ct);
        var group = new Context { Name = RequiredName(request.Name) };
        await SetGroupAsync(group, request, ct);
        db.Contexts.Add(group);
        await scope.SaveAsync(ct);
        return Group(group);
    }

    public async Task<GroupResponse> UpdateGroupAsync(Guid id, GroupRequest request, CancellationToken ct)
    {
        var (_, group) = await scope.GroupAsync(id, true, ct);
        if (group.EndsAt != null)
            throw Conflict();
        if (await db.ContextMembers.AnyAsync(m => m.ContextId == id && m.JoinedAt < request.StartsAt, ct)
            || await db.ContextLeaders.AnyAsync(l => l.ContextId == id && l.StartsAt < request.StartsAt, ct))
            throw Invalid();
        await SetGroupAsync(group, request, ct);
        group.Version = Guid.NewGuid();
        await scope.SaveAsync(ct);
        return Group(group);
    }

    public async Task<GroupResponse> EndGroupAsync(Guid id, EndRequest request, CancellationToken ct)
    {
        var (_, group) = await scope.GroupAsync(id, true, ct);
        if (group.EndsAt != null)
            throw Conflict();
        if (request.EndsAt < group.StartsAt
            || await db.ContextMembers.AnyAsync(m => m.ContextId == id && (m.JoinedAt > request.EndsAt || m.LeftAt > request.EndsAt), ct)
            || await db.ContextLeaders.AnyAsync(l => l.ContextId == id && (l.StartsAt > request.EndsAt || l.EndsAt > request.EndsAt), ct))
            throw Invalid();
        group.EndsAt = request.EndsAt;
        group.Version = Guid.NewGuid();
        await scope.SaveAsync(ct);
        return Group(group);
    }
}
