using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

public interface IHouseholdService
{
    Task<HouseholdResponse> GetHouseholdAsync(Guid id, CancellationToken ct);
    Task<HouseholdResponse> CreateHouseholdAsync(HouseholdRequest request, CancellationToken ct);
    Task<HouseholdResponse> UpdateHouseholdAsync(Guid id, Guid expectedVersion, HouseholdRequest request, CancellationToken ct);
}

public sealed class HouseholdService(ApplicationDbContext db, TrackerScope scope) : IHouseholdService
{
    private async Task<HouseholdResponse> ResponseAsync(Household household, CancellationToken ct) => new(household.Id, household.Label,
        await db.People.Where(p => p.HouseholdId == household.Id).OrderBy(p => p.Id)
            .Select(p => new HouseholdMemberResponse(p.Id, p.FirstName)).ToArrayAsync(ct),
        household.Version);

    public async Task<HouseholdResponse> GetHouseholdAsync(Guid id, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        await scope.RequireHouseholdAsync(id, actor, ct);
        var household = await db.Households.AsNoTracking().SingleOrDefaultAsync(h => h.Id == id, ct) ?? throw Missing();
        return await ResponseAsync(household, ct);
    }

    public async Task<HouseholdResponse> CreateHouseholdAsync(HouseholdRequest request, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        var household = new Household { Label = Optional(request.Label, 200) };
        await SetMembersAsync(household, request, actor, creating: true, ct);
        db.Households.Add(household);
        await scope.SaveAsync(ct);
        return await ResponseAsync(household, ct);
    }

    public async Task<HouseholdResponse> UpdateHouseholdAsync(Guid id, Guid expectedVersion, HouseholdRequest request, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        await scope.RequireHouseholdAsync(id, actor, ct);
        var household = await db.Households.SingleOrDefaultAsync(h => h.Id == id, ct) ?? throw Missing();
        RequireVersion(household.Version, expectedVersion);
        await SetMembersAsync(household, request, actor, creating: false, ct);
        household.Version = Guid.NewGuid();
        await scope.SaveAsync(ct, TrackerFailure.PreconditionFailed);
        return await ResponseAsync(household, ct);
    }

    private async Task SetMembersAsync(Household household, HouseholdRequest request, ChurchActor actor, bool creating, CancellationToken ct)
    {
        if (request.MemberIds is null || request.MemberIds.Length > 200 || request.MemberIds.Distinct().Count() != request.MemberIds.Length)
            throw Invalid();
        var members = await db.People.Where(p => request.MemberIds.Contains(p.Id)).ToArrayAsync(ct);
        if (members.Length != request.MemberIds.Length)
            throw Invalid();
        if (!TrackerScope.ManagesAll(actor))
        {
            var accessibleIds = await scope.People(actor).Where(p => request.MemberIds.Contains(p.Id)).Select(p => p.Id).ToArrayAsync(ct);
            // A new household must include someone the actor can see. Non-Staff may only submit people they can see,
            // or people already in the household being updated.
            if ((creating && accessibleIds.Length == 0)
                || members.Any(p => !accessibleIds.Contains(p.Id) && (creating || p.HouseholdId != household.Id)))
                throw Denied();
        }
        var otherHouseholdIds = members.Where(p => p.HouseholdId != null && p.HouseholdId != household.Id)
            .Select(p => p.HouseholdId.GetValueOrDefault()).Distinct().ToArray();
        foreach (var id in otherHouseholdIds)
            await scope.RequireHouseholdAsync(id, actor, ct);
        // Households losing a member change too, so their versions move and a concurrent edit of them is rejected.
        foreach (var other in await db.Households.Where(h => otherHouseholdIds.Contains(h.Id)).ToArrayAsync(ct))
            other.Version = Guid.NewGuid();

        var previous = creating ? [] : await db.People.Where(p => p.HouseholdId == household.Id).ToArrayAsync(ct);
        var originalHousehold = previous.Concat(members).DistinctBy(p => p.Id).ToDictionary(p => p.Id, p => p.HouseholdId);
        foreach (var person in previous)
            person.HouseholdId = null;
        foreach (var person in members)
            person.HouseholdId = household.Id;
        // A person's profile includes HouseholdId, so moving them changes their version too.
        foreach (var (personId, before) in originalHousehold)
        {
            var person = members.FirstOrDefault(p => p.Id == personId) ?? previous.First(p => p.Id == personId);
            if (person.HouseholdId != before)
                person.Version = Guid.NewGuid();
        }
        var suppliedLabel = Optional(request.Label, 200);
        var firstMember = request.MemberIds.Length == 0 ? null : members.Single(p => p.Id == request.MemberIds[0]);
        household.Label = suppliedLabel ?? household.Label
            ?? (firstMember?.LastName is { } surname ? $"The {surname} Family" : null);
    }
}
