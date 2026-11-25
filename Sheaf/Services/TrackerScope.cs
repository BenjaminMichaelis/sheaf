using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;

namespace Sheaf.Services;

/// <summary>
/// Resolves the current tracker actor from persisted membership and owns the row-level visibility rules
/// (which groups and people an actor may see). Every tracker service goes through this type so the rules live in one place.
/// </summary>
public sealed class TrackerScope(ApplicationDbContext db, ICurrentChurchActor currentActor, IAuthorizationService authorization, TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>Returns the current actor, re-validated against persisted membership, or throws Forbidden.</summary>
    public async Task<ChurchActor> ActorAsync(CancellationToken ct)
    {
        var actor = await currentActor.GetAsync(ct) ?? throw TrackerRules.Denied();
        await AuthorizeAsync(actor, SheafPolicies.TrackerActor);
        return actor;
    }

    /// <summary>Returns the current actor if they are Staff, or throws Forbidden.</summary>
    public async Task<ChurchActor> StaffAsync(CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        await AuthorizeAsync(actor, SheafPolicies.TrackerStaff);
        return actor;
    }

    private async Task AuthorizeAsync(ChurchActor actor, string policy)
    {
        if (!(await authorization.AuthorizeAsync(actor.Principal, actor, policy)).Succeeded)
            throw TrackerRules.Denied();
    }

    /// <summary>Staff see every group; Leaders see only active groups they currently lead.</summary>
    public IQueryable<Context> Groups(ChurchActor actor)
    {
        if (actor.Role == ChurchMembershipRole.Staff)
            return db.Contexts;
        var now = Now;
        return db.Contexts.Where(g => g.StartsAt <= now && (g.EndsAt == null || g.EndsAt > now)
            && db.ContextLeaders.Any(l => l.ContextId == g.Id && l.StartsAt <= now && (l.EndsAt == null || l.EndsAt > now)
                && db.People.Any(p => p.Id == l.PersonId && p.UserId == actor.UserId)));
    }

    /// <summary>Staff see every person; Leaders see current members and leaders of the groups they lead.</summary>
    public IQueryable<Person> People(ChurchActor actor)
    {
        if (actor.Role == ChurchMembershipRole.Staff)
            return db.People;
        var groups = Groups(actor).Select(g => g.Id);
        var now = Now;
        return db.People.Where(p => db.ContextMembers.Any(m => m.PersonId == p.Id && groups.Contains(m.ContextId)
                && m.JoinedAt <= now && (m.LeftAt == null || m.LeftAt > now))
            || db.ContextLeaders.Any(l => l.PersonId == p.Id && groups.Contains(l.ContextId)
                && l.StartsAt <= now && (l.EndsAt == null || l.EndsAt > now)));
    }

    /// <summary>Loads a group visible to the actor (optionally requiring Staff), or throws NotFound.</summary>
    public async Task<(ChurchActor Actor, Context Group)> GroupAsync(Guid id, bool staffOnly, CancellationToken ct)
    {
        var actor = staffOnly ? await StaffAsync(ct) : await ActorAsync(ct);
        var group = await Groups(actor).SingleOrDefaultAsync(g => g.Id == id, ct) ?? throw TrackerRules.Missing();
        return (actor, group);
    }

    /// <summary>Throws NotFound unless the actor is Staff or can see at least one person in the household.</summary>
    public async Task RequireHouseholdAsync(Guid id, ChurchActor actor, CancellationToken ct)
    {
        if (actor.Role != ChurchMembershipRole.Staff
            && !await People(actor).AnyAsync(p => p.HouseholdId == id, ct))
            throw TrackerRules.Missing();
    }

    /// <summary>Saves changes, translating write races and constraint failures into Conflict.</summary>
    public async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // DbUpdateConcurrencyException is a DbUpdateException: stale versions and constraint races both mean
            // the stored state changed underneath this request.
            throw TrackerRules.Conflict();
        }
    }
}

/// <summary>Shared tracker failures and input normalization.</summary>
internal static class TrackerRules
{
    public static TrackerException Missing() => new(TrackerFailure.NotFound);
    public static TrackerException Denied() => new(TrackerFailure.Forbidden);
    public static TrackerException Invalid() => new(TrackerFailure.InvalidInput);
    public static TrackerException Conflict() => new(TrackerFailure.Conflict);

    public static string RequiredName(string? value, int length = 200)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > length)
            throw Invalid();
        return value.Trim();
    }

    public static string? Optional(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.Trim().Length > length)
            throw Invalid();
        return value.Trim();
    }
}
