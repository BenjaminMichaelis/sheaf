using System.Security.Claims;

using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

namespace Sheaf.Services;

public sealed class TrackerException(int statusCode, string title) : Exception(title)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class TrackerService(ApplicationDbContext db, ICurrentChurch church, IHttpContextAccessor http, TimeProvider clock) : ITrackerService
{
    private static TrackerException Missing() => new(404, "Resource not found.");
    private static TrackerException Denied() => new(403, "Access denied.");
    private static TrackerException Invalid() => new(400, "Invalid tracker input.");
    private static TrackerException Conflict() => new(409, "The resource changed or the group has ended.");

    private async Task<ChurchMembership> ActorAsync(CancellationToken ct)
    {
        var principal = http.HttpContext?.User;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal?.Identity?.IsAuthenticated != true || userId is null || church.LeaderId is not { } membershipId)
            throw Denied();
        var actor = await db.ChurchMemberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == membershipId && m.UserId == userId, ct);
        if (actor is null || actor.Role is not (ChurchMembershipRole.Staff or ChurchMembershipRole.Leader))
            throw Denied();
        return actor;
    }

    private async Task StaffAsync(CancellationToken ct)
    {
        if ((await ActorAsync(ct)).Role != ChurchMembershipRole.Staff)
            throw Denied();
    }

    private IQueryable<Context> AccessibleGroups(ChurchMembership actor)
    {
        if (actor.Role == ChurchMembershipRole.Staff)
            return db.Contexts;
        var now = clock.GetUtcNow();
        return db.Contexts.Where(g => g.StartsAt <= now && (g.EndsAt == null || g.EndsAt > now)
            && db.ContextLeaders.Any(l => l.ContextId == g.Id && l.StartsAt <= now && (l.EndsAt == null || l.EndsAt > now)
                && db.People.Any(p => p.Id == l.PersonId && p.UserId == actor.UserId)));
    }

    private IQueryable<Person> AccessiblePeople(ChurchMembership actor)
    {
        if (actor.Role == ChurchMembershipRole.Staff)
            return db.People;
        var groups = AccessibleGroups(actor).Select(g => g.Id);
        var now = clock.GetUtcNow();
        return db.People.Where(p => db.ContextMembers.Any(m => m.PersonId == p.Id && groups.Contains(m.ContextId)
                && m.JoinedAt <= now && (m.LeftAt == null || m.LeftAt > now))
            || db.ContextLeaders.Any(l => l.PersonId == p.Id && groups.Contains(l.ContextId)
                && l.StartsAt <= now && (l.EndsAt == null || l.EndsAt > now)));
    }

    private async Task<Context> GroupAsync(Guid id, bool staff, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        if (staff && actor.Role != ChurchMembershipRole.Staff)
            throw Denied();
        return await AccessibleGroups(actor).SingleOrDefaultAsync(g => g.Id == id, ct) ?? throw Missing();
    }

    private static string RequiredName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 200)
            throw Invalid();
        return value.Trim();
    }

    private static string? Optional(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.Trim().Length > length)
            throw Invalid();
        return value.Trim();
    }

    private static void SetPerson(Person person, PersonRequest request)
    {
        person.Name = RequiredName(request.Name);
        person.GivenName = Optional(request.GivenName, 100);
        person.Email = Optional(request.Email, 320);
        person.Phone = Optional(request.Phone, 50);
        person.IsActive = request.IsActive;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict();
        }
    }

    private static PersonResponse Profile(Person p) => new(p.Id, p.Name, p.GivenName, p.Email, p.Phone, p.HouseholdId, p.IsActive);
    private static GroupResponse Group(Context g) => new(g.Id, g.Name, g.StartsAt, g.EndsAt, g.GroupTypeId);

    public async Task<PersonResponse> CreatePersonAsync(PersonRequest request, CancellationToken ct)
    {
        await StaffAsync(ct);
        var person = new Person { Name = RequiredName(request.Name) };
        SetPerson(person, request);
        db.People.Add(person);
        await SaveAsync(ct);
        return Profile(person);
    }

    public async Task<PersonResponse> GetPersonAsync(Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        return Profile(await AccessiblePeople(actor).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Missing());
    }

    public async Task<PersonResponse> UpdatePersonAsync(Guid id, PersonRequest request, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var person = await AccessiblePeople(actor).SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Missing();
        SetPerson(person, request);
        await SaveAsync(ct);
        return Profile(person);
    }

    public async Task<PersonLookupResponse[]> LookupAsync(string? name, CancellationToken ct)
    {
        await ActorAsync(ct);
        var term = Optional(name, 200);
        var now = clock.GetUtcNow();
        return await db.People.Where(p => p.IsActive && (term == null || p.Name.Contains(term)))
            .OrderBy(p => p.Name).ThenBy(p => p.Id).Take(50)
            .Select(p => new PersonLookupResponse(p.Id, p.Name,
                db.Contexts.Where(g => g.StartsAt <= now && (g.EndsAt == null || g.EndsAt > now)
                    && (db.ContextMembers.Any(m => m.PersonId == p.Id && m.ContextId == g.Id && m.JoinedAt <= now && (m.LeftAt == null || m.LeftAt > now))
                        || db.ContextLeaders.Any(l => l.PersonId == p.Id && l.ContextId == g.Id && l.StartsAt <= now && (l.EndsAt == null || l.EndsAt > now))))
                    .OrderBy(g => g.Name).Select(g => g.Name).ToArray(),
                db.Households.Where(h => h.Id == p.HouseholdId).Select(h => h.Label).FirstOrDefault(),
                db.People.Where(member => p.HouseholdId != null && member.HouseholdId == p.HouseholdId && member.GivenName != null)
                    .OrderBy(member => member.Id).Select(member => member.GivenName ?? string.Empty).ToArray()))
            .ToArrayAsync(ct);
    }

    public async Task<GroupTypeResponse[]> GetTypesAsync(CancellationToken ct)
    {
        await ActorAsync(ct);
        return await db.GroupTypes.OrderBy(t => t.Name).ThenBy(t => t.Id).Take(200)
            .Select(t => new GroupTypeResponse(t.Id, t.Name)).ToArrayAsync(ct);
    }

    public async Task<GroupTypeResponse> GetTypeAsync(Guid id, CancellationToken ct)
    {
        await ActorAsync(ct);
        return await db.GroupTypes.Where(t => t.Id == id).Select(t => new GroupTypeResponse(t.Id, t.Name))
            .SingleOrDefaultAsync(ct) ?? throw Missing();
    }

    public async Task<GroupTypeResponse> CreateTypeAsync(GroupTypeRequest request, CancellationToken ct)
    {
        await StaffAsync(ct);
        var type = new GroupType { Name = RequiredName(request.Name) };
        db.GroupTypes.Add(type);
        await SaveAsync(ct);
        return new(type.Id, type.Name);
    }

    public async Task<GroupTypeResponse> UpdateTypeAsync(Guid id, GroupTypeRequest request, CancellationToken ct)
    {
        await StaffAsync(ct);
        var type = await db.GroupTypes.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw Missing();
        type.Name = RequiredName(request.Name);
        await SaveAsync(ct);
        return new(type.Id, type.Name);
    }

    public async Task<GroupResponse[]> GetGroupsAsync(CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        return await AccessibleGroups(actor).OrderBy(g => g.Name).ThenBy(g => g.Id).Take(200)
            .Select(g => new GroupResponse(g.Id, g.Name, g.StartsAt, g.EndsAt, g.GroupTypeId)).ToArrayAsync(ct);
    }

    public async Task<GroupResponse> GetGroupAsync(Guid id, CancellationToken ct) => Group(await GroupAsync(id, false, ct));

    private async Task SetGroupAsync(Context group, GroupRequest request, CancellationToken ct)
    {
        group.Name = RequiredName(request.Name);
        if (request.StartsAt == default)
            throw Invalid();
        if (request.GroupTypeId is { } typeId && !await db.GroupTypes.AnyAsync(t => t.Id == typeId, ct))
            throw Missing();
        group.StartsAt = request.StartsAt;
        group.GroupTypeId = request.GroupTypeId;
    }

    public async Task<GroupResponse> CreateGroupAsync(GroupRequest request, CancellationToken ct)
    {
        await StaffAsync(ct);
        var group = new Context { Name = RequiredName(request.Name) };
        await SetGroupAsync(group, request, ct);
        db.Contexts.Add(group);
        await SaveAsync(ct);
        return Group(group);
    }

    public async Task<GroupResponse> UpdateGroupAsync(Guid id, GroupRequest request, CancellationToken ct)
    {
        var group = await GroupAsync(id, true, ct);
        if (group.EndsAt != null)
            throw Conflict();
        if (await db.ContextMembers.AnyAsync(m => m.ContextId == id && m.JoinedAt < request.StartsAt, ct)
            || await db.ContextLeaders.AnyAsync(l => l.ContextId == id && l.StartsAt < request.StartsAt, ct))
            throw Invalid();
        await SetGroupAsync(group, request, ct);
        group.Version = Guid.NewGuid();
        await SaveAsync(ct);
        return Group(group);
    }

    public async Task<GroupResponse> EndGroupAsync(Guid id, EndRequest request, CancellationToken ct)
    {
        var group = await GroupAsync(id, true, ct);
        if (group.EndsAt != null)
            throw Conflict();
        if (request.EndsAt < group.StartsAt
            || await db.ContextMembers.AnyAsync(m => m.ContextId == id && (m.JoinedAt > request.EndsAt || m.LeftAt > request.EndsAt), ct)
            || await db.ContextLeaders.AnyAsync(l => l.ContextId == id && (l.StartsAt > request.EndsAt || l.EndsAt > request.EndsAt), ct))
            throw Invalid();
        group.EndsAt = request.EndsAt;
        group.Version = Guid.NewGuid();
        await SaveAsync(ct);
        return Group(group);
    }

    public async Task<AssignmentResponse[]> GetAssignmentsAsync(Guid groupId, bool leaders, CancellationToken ct)
    {
        await GroupAsync(groupId, false, ct);
        if (leaders)
            return await db.ContextLeaders.Where(l => l.ContextId == groupId).OrderBy(l => l.StartsAt).ThenBy(l => l.Id)
                .Select(l => new AssignmentResponse(l.Id, l.PersonId, l.StartsAt, l.EndsAt)).ToArrayAsync(ct);
        return await db.ContextMembers.Where(m => m.ContextId == groupId).OrderBy(m => m.JoinedAt).ThenBy(m => m.Id)
            .Select(m => new AssignmentResponse(m.Id, m.PersonId, m.JoinedAt, m.LeftAt)).ToArrayAsync(ct);
    }

    public async Task<AssignmentResponse> GetAssignmentAsync(Guid groupId, Guid assignmentId, bool leaders, CancellationToken ct)
    {
        await GroupAsync(groupId, false, ct);
        if (leaders)
            return await db.ContextLeaders.Where(l => l.ContextId == groupId && l.Id == assignmentId)
                .Select(l => new AssignmentResponse(l.Id, l.PersonId, l.StartsAt, l.EndsAt)).SingleOrDefaultAsync(ct) ?? throw Missing();
        return await db.ContextMembers.Where(m => m.ContextId == groupId && m.Id == assignmentId)
            .Select(m => new AssignmentResponse(m.Id, m.PersonId, m.JoinedAt, m.LeftAt)).SingleOrDefaultAsync(ct) ?? throw Missing();
    }

    public async Task<AssignmentResponse> AddAssignmentAsync(Guid groupId, AssignmentRequest request, bool leaders, CancellationToken ct)
    {
        var group = await GroupAsync(groupId, leaders, ct);
        if (group.EndsAt != null)
            throw Conflict();
        if (request.StartsAt < group.StartsAt || !await db.People.AnyAsync(p => p.Id == request.PersonId, ct))
            throw Invalid();
        group.Version = Guid.NewGuid();
        AssignmentResponse result;
        if (leaders)
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
        await SaveAsync(ct);
        return result;
    }

    public async Task<AssignmentResponse> EndAssignmentAsync(Guid groupId, Guid assignmentId, EndRequest request, bool leaders, CancellationToken ct)
    {
        var group = await GroupAsync(groupId, leaders, ct);
        if (group.EndsAt is { } end && request.EndsAt > end)
            throw Invalid();
        group.Version = Guid.NewGuid();
        AssignmentResponse result;
        if (leaders)
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
        await SaveAsync(ct);
        return result;
    }

    private async Task HouseholdAccessAsync(Guid id, ChurchMembership actor, CancellationToken ct)
    {
        if (actor.Role != ChurchMembershipRole.Staff
            && !await AccessiblePeople(actor).AnyAsync(p => p.HouseholdId == id, ct))
            throw Missing();
    }

    private async Task<HouseholdResponse> HouseholdResponseAsync(Household household, CancellationToken ct) => new(household.Id, household.Label,
        await db.People.Where(p => p.HouseholdId == household.Id).OrderBy(p => p.Id)
            .Select(p => new HouseholdMemberResponse(p.Id, p.GivenName)).ToArrayAsync(ct));

    public async Task<HouseholdResponse> GetHouseholdAsync(Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        await HouseholdAccessAsync(id, actor, ct);
        var household = await db.Households.AsNoTracking().SingleOrDefaultAsync(h => h.Id == id, ct) ?? throw Missing();
        return await HouseholdResponseAsync(household, ct);
    }

    public async Task<HouseholdResponse> CreateHouseholdAsync(HouseholdRequest request, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var household = new Household { Label = Optional(request.Label, 200) };
        await SetHouseholdAsync(household, request, actor, true, ct);
        db.Households.Add(household);
        await SaveAsync(ct);
        return await HouseholdResponseAsync(household, ct);
    }

    public async Task<HouseholdResponse> UpdateHouseholdAsync(Guid id, HouseholdRequest request, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        await HouseholdAccessAsync(id, actor, ct);
        var household = await db.Households.SingleOrDefaultAsync(h => h.Id == id, ct) ?? throw Missing();
        await SetHouseholdAsync(household, request, actor, false, ct);
        await SaveAsync(ct);
        return await HouseholdResponseAsync(household, ct);
    }

    private async Task SetHouseholdAsync(Household household, HouseholdRequest request, ChurchMembership actor, bool creating, CancellationToken ct)
    {
        if (request.MemberIds is null || request.MemberIds.Length > 200 || request.MemberIds.Distinct().Count() != request.MemberIds.Length)
            throw Invalid();
        if (creating && actor.Role != ChurchMembershipRole.Staff && !await AccessiblePeople(actor).AnyAsync(p => request.MemberIds.Contains(p.Id), ct))
            throw Denied();
        var members = await db.People.Where(p => request.MemberIds.Contains(p.Id)).ToArrayAsync(ct);
        if (members.Length != request.MemberIds.Length)
            throw Invalid();
        foreach (var previousId in members.Where(p => p.HouseholdId != null && p.HouseholdId != household.Id).Select(p => p.HouseholdId).Distinct())
            if (previousId is { } id)
                await HouseholdAccessAsync(id, actor, ct);
        var previous = creating ? [] : await db.People.Where(p => p.HouseholdId == household.Id).ToArrayAsync(ct);
        foreach (var person in previous)
            person.HouseholdId = null;
        foreach (var person in members)
            person.HouseholdId = household.Id;
        household.Label = Optional(request.Label, 200);
    }
}
