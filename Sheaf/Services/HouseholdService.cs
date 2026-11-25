using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

public interface IHouseholdService
{
    Task<HouseholdResponse> GetHouseholdAsync(Guid id, CancellationToken ct);
    Task<HouseholdResponse> CreateHouseholdAsync(HouseholdRequest request, CancellationToken ct);
    Task<HouseholdResponse> UpdateHouseholdAsync(Guid id, HouseholdRequest request, CancellationToken ct);
}

public sealed class HouseholdService(ApplicationDbContext db, TrackerScope scope) : IHouseholdService
{
    private async Task<HouseholdResponse> ResponseAsync(Household household, CancellationToken ct) => new(household.Id, household.Label,
        await db.People.Where(p => p.HouseholdId == household.Id).OrderBy(p => p.Id)
            .Select(p => new HouseholdMemberResponse(p.Id, p.FirstName)).ToArrayAsync(ct));

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

    public async Task<HouseholdResponse> UpdateHouseholdAsync(Guid id, HouseholdRequest request, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        await scope.RequireHouseholdAsync(id, actor, ct);
        var household = await db.Households.SingleOrDefaultAsync(h => h.Id == id, ct) ?? throw Missing();
        await SetMembersAsync(household, request, actor, creating: false, ct);
        await scope.SaveAsync(ct);
        return await ResponseAsync(household, ct);
    }

    private async Task SetMembersAsync(Household household, HouseholdRequest request, ChurchActor actor, bool creating, CancellationToken ct)
    {
        if (request.MemberIds is null || request.MemberIds.Length > 200 || request.MemberIds.Distinct().Count() != request.MemberIds.Length)
            throw Invalid();
        var members = await db.People.Where(p => request.MemberIds.Contains(p.Id)).ToArrayAsync(ct);
        if (members.Length != request.MemberIds.Length)
            throw Invalid();
        if (actor.Role != ChurchMembershipRole.Staff)
        {
            var accessibleIds = await scope.People(actor).Where(p => request.MemberIds.Contains(p.Id)).Select(p => p.Id).ToArrayAsync(ct);
            // A new household must include someone the actor can see. Non-Staff may only submit people they can see,
            // or people already in the household being updated.
            if ((creating && accessibleIds.Length == 0)
                || members.Any(p => !accessibleIds.Contains(p.Id) && (creating || p.HouseholdId != household.Id)))
                throw Denied();
        }
        foreach (var previousId in members.Where(p => p.HouseholdId != null && p.HouseholdId != household.Id).Select(p => p.HouseholdId).Distinct())
            if (previousId is { } id)
                await scope.RequireHouseholdAsync(id, actor, ct);
        var previous = creating ? [] : await db.People.Where(p => p.HouseholdId == household.Id).ToArrayAsync(ct);
        foreach (var person in previous)
            person.HouseholdId = null;
        foreach (var person in members)
            person.HouseholdId = household.Id;
        var suppliedLabel = Optional(request.Label, 200);
        var firstMember = request.MemberIds.Length == 0 ? null : members.Single(p => p.Id == request.MemberIds[0]);
        household.Label = suppliedLabel ?? household.Label
            ?? (firstMember?.LastName is { } surname ? $"The {surname} Family" : null);
    }
}
