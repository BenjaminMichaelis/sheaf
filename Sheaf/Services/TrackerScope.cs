using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;

namespace Sheaf.Services;

/// <summary>
/// Owns the row-level visibility rules: which groups, people, and households an actor may see. Which operations a church
/// role may call is declared on the endpoints (<see cref="Permission.TrackerAccess"/>, <see cref="Permission.TrackerManage"/>, and <see cref="Permission.PeopleManage"/>);
/// this type answers the questions that need the data loaded, including what the actor's group roles allow
/// (<see cref="GroupPermission"/>), so every tracker service goes through it.
/// </summary>
public sealed partial class TrackerScope(ApplicationDbContext db, ICurrentChurchActor currentActor, TimeProvider clock, ILogger<TrackerScope> logger)
{
    /// <summary>How long after attending one of a Leader's groups a person's basic profile stays visible to that Leader.</summary>
    public const int AttendanceVisibilityDays = 90;
    /// <summary>How long after creating a person a Leader may still correct their details.</summary>
    public const int CreatorEditDays = 7;

    private ChurchCalendar? _calendar;

    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>
    /// The current church's calendar, loaded by <see cref="ActorAsync"/>. Every <see cref="ChurchActor"/> a tracker
    /// service holds came from <see cref="ActorAsync"/>, so the calendar is always loaded by the time a rule needs it.
    /// </summary>
    public ChurchCalendar Calendar => _calendar
        ?? throw new InvalidOperationException($"Call {nameof(ActorAsync)} before reading the church calendar.");

    /// <summary>The church's local date now.</summary>
    public DateOnly Today => Calendar.DateOf(Now);

    /// <summary>
    /// Returns the current actor from persisted membership, or throws Forbidden if it no longer exists. Also loads the
    /// church's calendar, which visibility and attendance rules use to compare meeting dates with instants.
    /// </summary>
    public async Task<ChurchActor> ActorAsync(CancellationToken ct)
    {
        var actor = await currentActor.GetAsync(ct) ?? throw TrackerRules.Denied();
        if (_calendar is null)
        {
            var zone = await db.Churches.Where(c => c.Id == actor.ChurchId).Select(c => c.TimeZoneId).SingleAsync(ct);
            // A zone that resolved when saved can fail on a host with different time-zone data. Falling back to another
            // zone would quietly move meeting dates, so fail clearly instead.
            if (!ChurchCalendar.TryCreate(zone, out _calendar))
            {
                LogUnresolvableTimeZone(logger, actor.ChurchId, zone);
                throw new TrackerException(TrackerFailure.TimeZoneUnavailable);
            }
        }
        return actor;
    }

    /// <summary>Whether the actor manages every group rather than only the groups they lead.</summary>
    public static bool ManagesGroups(ChurchActor actor) => RolePermissions.HasPermission(actor.Role, Permission.TrackerManage);

    /// <summary>Whether the actor manages every person and household rather than only those their groups reach.</summary>
    public static bool ManagesPeople(ChurchActor actor) => RolePermissions.HasPermission(actor.Role, Permission.PeopleManage);

    /// <summary>
    /// Groups in which the actor may use <paramref name="permission"/>. Managers reach every group; everyone else reaches
    /// active groups where their own person currently holds a role that grants it (<see cref="GroupRolePermissions"/>).
    /// This is the only place a group role becomes access, so list filters and single-group checks always agree.
    /// </summary>
    public IQueryable<Group> Groups(ChurchActor actor, GroupPermission permission)
    {
        if (ManagesGroups(actor))
            return db.Groups;
        var roles = GroupRolePermissions.RolesWith(permission).ToArray();
        var now = Now;
        var granting = db.GroupAssignments.CurrentAt(now)
            .Where(a => roles.Contains(a.Role) && db.People.Any(p => p.Id == a.PersonId && p.UserId == actor.UserId));
        return db.Groups.ActiveAt(now).Where(g => granting.Any(a => a.GroupId == g.Id));
    }

    /// <summary>
    /// People who are records of their own. A record merged into another (<see cref="Person.MergedIntoId"/>) is kept as
    /// history only: nobody can change it, assign it, or put it in a household, and only managers can still read it.
    /// </summary>
    private IQueryable<Person> Unmerged => db.People.Where(p => p.MergedIntoId == null);

    /// <summary>
    /// People the actor may assign to a group or put in a household, and change (see also <see cref="EditablePeople"/>).
    /// Managers reach every person; everyone else reaches whoever is currently on the roster, in any role, of a group
    /// where the actor may manage members.
    /// </summary>
    public IQueryable<Person> People(ChurchActor actor)
    {
        if (ManagesPeople(actor))
            return Unmerged;
        var groups = Groups(actor, GroupPermission.ManageMembers).Select(g => g.Id);
        var now = Now;
        var assigned = db.GroupAssignments.CurrentAt(now);
        return Unmerged.Where(p => assigned.Any(a => a.PersonId == p.Id && groups.Contains(a.GroupId)));
    }

    /// <summary>
    /// People whose name and contact details the actor may correct: everyone in <see cref="People"/>, plus people the
    /// actor created in the last <see cref="CreatorEditDays"/> days, so a Leader can fix a typo in someone they just
    /// added while recording attendance. Creating someone never lets a Leader add them to a roster or household.
    /// </summary>
    public IQueryable<Person> EditablePeople(ChurchActor actor)
    {
        if (ManagesPeople(actor))
            return Unmerged;
        var createdSince = Now.AddDays(-CreatorEditDays);
        var reachable = People(actor).Select(p => p.Id);
        return Unmerged.Where(p => reachable.Contains(p.Id)
            || (p.CreatedByUserId == actor.UserId && p.CreatedAt >= createdSince));
    }

    /// <summary>
    /// People whose basic profile the actor may read: everyone in <see cref="EditablePeople"/>, plus anyone still marked present at
    /// one of the actor's groups' meetings in the last <see cref="AttendanceVisibilityDays"/> days. Managers also read
    /// merged records, whose profile names the person they were merged into. Attending grants read
    /// access only, so marking someone present never lets a Leader edit them or add them to a roster or household.
    /// </summary>
    public IQueryable<Person> VisiblePeople(ChurchActor actor)
    {
        if (ManagesPeople(actor))
            return db.People;
        var groups = Groups(actor, GroupPermission.View).Select(g => g.Id);
        var attendedSince = Today.AddDays(-AttendanceVisibilityDays);
        var reachable = EditablePeople(actor).Select(p => p.Id);
        return db.People.Where(p => reachable.Contains(p.Id)
            || db.Attendances.Any(a => a.PersonId == p.Id && a.RemovedAt == null
                && db.GroupMeetings.Any(m => m.Id == a.MeetingId && groups.Contains(m.GroupId) && m.OccurredOn >= attendedSince)));
    }

    /// <summary>
    /// Loads a group in which the actor may use <paramref name="permission"/>, or throws NotFound. A group the actor can
    /// see but not act on reads as not found too, so the response never says which groups exist.
    /// </summary>
    public async Task<(ChurchActor Actor, Group Group)> GroupAsync(Guid id, GroupPermission permission, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var group = await Groups(actor, permission).SingleOrDefaultAsync(g => g.Id == id, ct) ?? throw TrackerRules.Missing();
        return (actor, group);
    }

    /// <summary>Throws NotFound unless the actor manages all or can see at least one person in the household.</summary>
    public async Task RequireHouseholdAsync(Guid id, ChurchActor actor, CancellationToken ct)
    {
        if (!ManagesPeople(actor)
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Church {ChurchId} has time zone {TimeZoneId}, which this host cannot resolve.")]
    private static partial void LogUnresolvableTimeZone(ILogger logger, Guid churchId, string timeZoneId);
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

/// <summary>
/// The one definition of "current" for groups and group assignments. Access rules and lookups share it, so a person is
/// never shown as in a group they cannot be reached through, or the reverse.
/// </summary>
internal static class TrackerPeriods
{
    /// <summary>Groups that have started and not yet ended at <paramref name="now"/>.</summary>
    public static IQueryable<Group> ActiveAt(this IQueryable<Group> groups, DateTimeOffset now) =>
        groups.Where(g => g.StartsAt <= now && (g.EndsAt == null || g.EndsAt > now));

    /// <summary>Assignments, in any role, that have begun and not yet ended at <paramref name="now"/>.</summary>
    public static IQueryable<GroupAssignment> CurrentAt(this IQueryable<GroupAssignment> assignments, DateTimeOffset now) =>
        assignments.Where(a => a.StartsAt <= now && (a.EndsAt == null || a.EndsAt > now));
}

/// <summary>Shared tracker failures and input normalization.</summary>
internal static class TrackerRules
{
    /// <summary>The trimmed value, or null when it is missing or only whitespace.</summary>
    public static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static TrackerException Missing() => new(TrackerFailure.NotFound);
    public static TrackerException Denied() => new(TrackerFailure.Forbidden);
    public static TrackerException Invalid() => new(TrackerFailure.InvalidInput);
    public static TrackerException Conflict() => new(TrackerFailure.Conflict);

    /// <summary>Nothing is saved; <paramref name="reasons"/> become ProblemDetails members explaining what to confirm.</summary>
    public static TrackerException ConfirmationRequired(IDictionary<string, object?> reasons) =>
        new(TrackerFailure.ConfirmationRequired, reasons);

    public static string RequiredName(string? value, int length = 200)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > length)
            throw Invalid();
        return value.Trim();
    }

    public static string? Optional(string? value, int length)
    {
        var trimmed = Trimmed(value);
        return trimmed?.Length > length ? throw Invalid() : trimmed;
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
