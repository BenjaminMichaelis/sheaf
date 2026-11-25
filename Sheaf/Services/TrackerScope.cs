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

    /// <summary>
    /// Saves changes. A stale concurrency token becomes <paramref name="staleFailure"/> (PreconditionFailed for If-Match
    /// writes, Conflict otherwise); any other write failure, such as a constraint race, becomes Conflict.
    /// </summary>
    public async Task SaveAsync(CancellationToken ct, TrackerFailure staleFailure = TrackerFailure.Conflict)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new TrackerException(staleFailure);
        }
        catch (DbUpdateException)
        {
            throw TrackerRules.Conflict();
        }
    }
}

/// <summary>An offset page request, validated at the API boundary.</summary>
public readonly record struct PageRequest(int Offset, int Limit)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static PageRequest From(int? offset, int? limit)
    {
        var page = new PageRequest(offset ?? 0, limit ?? DefaultLimit);
        if (page.Offset < 0 || page.Limit < 1 || page.Limit > MaxLimit)
            throw TrackerRules.Invalid();
        return page;
    }
}

/// <summary>Parses and formats the entity tags carried by <see cref="IVersioned"/> resources.</summary>
public static class EntityTag
{
    /// <summary>Formats a version as a strong entity tag.</summary>
    public static string Format(Guid version) => $"\"{version:D}\"";

    /// <summary>
    /// Reads the version a client expects from If-Match. A missing header is PreconditionRequired; a weak, wildcard
    /// or malformed tag can never match a strong version, so it is PreconditionFailed.
    /// </summary>
    public static Guid ParseIfMatch(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            throw new TrackerException(TrackerFailure.PreconditionRequired);
        var value = header.Trim();
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"' || !Guid.TryParseExact(value[1..^1], "D", out var version))
            throw new TrackerException(TrackerFailure.PreconditionFailed);
        return version;
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

    /// <summary>Throws PreconditionFailed unless the loaded version is the one the client read.</summary>
    public static void RequireVersion(Guid current, Guid expected)
    {
        if (current != expected)
            throw new TrackerException(TrackerFailure.PreconditionFailed);
    }

    /// <summary>Reads one page from an already-ordered query, fetching one extra row to know whether more remain.</summary>
    public static async Task<PagedResponse<T>> PageAsync<T>(this IQueryable<T> ordered, PageRequest page, CancellationToken ct)
    {
        var rows = await ordered.Skip(page.Offset).Take(page.Limit + 1).ToArrayAsync(ct);
        return rows.Length > page.Limit
            ? new(rows[..page.Limit], page.Offset + page.Limit)
            : new(rows, null);
    }
}
