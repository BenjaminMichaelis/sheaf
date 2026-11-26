using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

public interface IAttendanceService
{
    Task<PagedResponse<SessionSummaryResponse>> GetSessionsAsync(Guid groupId, PageRequest page, CancellationToken ct);
    Task<SessionResponse> GetSessionAsync(Guid groupId, Guid sessionId, CancellationToken ct);
    /// <summary>Records a meeting. <c>Created</c> is false when a retry found the meeting already recorded under the request's id.</summary>
    Task<(SessionResponse Session, bool Created)> CreateSessionAsync(Guid groupId, SessionRequest request, CancellationToken ct);
    Task<SessionResponse> UpdateAttendeesAsync(Guid groupId, Guid sessionId, Guid expectedVersion, AttendeesRequest request, CancellationToken ct);
    Task<PagedResponse<AttendanceRecordResponse>> GetRecordsAsync(Guid? personId, string? userId, PageRequest page, CancellationToken ct);
}

/// <summary>
/// Group meetings and who came. Anyone who can see a group (its current Leaders, or Staff for every group) records
/// its meetings for any past date while the group existed; Leaders may change a meeting for
/// <see cref="LeaderEditDays"/> days after it was recorded, Staff at any time.
/// Taking someone off a meeting keeps the row as history, so the record of who was marked present, and by whom,
/// survives every edit.
/// </summary>
public sealed class AttendanceService(ApplicationDbContext db, TrackerScope scope) : IAttendanceService
{
    /// <summary>Days after a meeting is recorded that its group's Leaders may still change it.</summary>
    public const int LeaderEditDays = 7;
    /// <summary>Most people one meeting may list.</summary>
    public const int MaxAttendees = 500;
    /// <summary>Most people one request may create.</summary>
    public const int MaxNewPeople = 50;

    private static readonly Expression<Func<Person, SessionPersonResponse>> NameOf = p =>
        new SessionPersonResponse(p.Id, p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName));

    public async Task<PagedResponse<SessionSummaryResponse>> GetSessionsAsync(Guid groupId, PageRequest page, CancellationToken ct)
    {
        await scope.GroupAsync(groupId, ct);
        return await db.ContextSessions.Where(s => s.ContextId == groupId)
            .OrderByDescending(s => s.OccurredOn).ThenByDescending(s => s.RecordedAt).ThenByDescending(s => s.Id)
            .Select(s => new SessionSummaryResponse(s.Id, s.OccurredOn,
                db.Attendances.Count(a => a.SessionId == s.Id && a.RemovedAt == null), s.Version))
            .PageAsync(page, ct);
    }

    public async Task<SessionResponse> GetSessionAsync(Guid groupId, Guid sessionId, CancellationToken ct)
    {
        await scope.GroupAsync(groupId, ct);
        var session = await db.ContextSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.ContextId == groupId, ct) ?? throw Missing();
        return await ResponseAsync(session, ct);
    }

    public async Task<(SessionResponse Session, bool Created)> CreateSessionAsync(Guid groupId, SessionRequest request, CancellationToken ct)
    {
        var (actor, group) = await scope.GroupAsync(groupId, ct);
        if (request.Id == Guid.Empty)
            throw Invalid();
        if (request.Id is { } id
            && await db.ContextSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct) is { } recorded)
            return (await ReplayAsync(recorded, groupId, request, ct), false);
        // Any past date while the group existed may be recorded, so late paperwork can be caught up. The Leader edit
        // window applies only to changing a meeting once it is recorded.
        RequireMeetingDate(group, request.OccurredOn);

        var reasons = new Dictionary<string, object?>();
        if (!request.Confirm)
        {
            var sameDay = await db.ContextSessions.Where(s => s.ContextId == groupId && s.OccurredOn == request.OccurredOn)
                .OrderBy(s => s.RecordedAt).Select(s => s.Id).ToArrayAsync(ct);
            if (sameDay.Length > 0)
                reasons["existingSessionIds"] = sameDay;
        }
        var session = new ContextSession
        {
            Id = request.Id ?? Guid.CreateVersion7(),
            ContextId = groupId,
            OccurredOn = request.OccurredOn,
            RecordedAt = scope.Now,
            RecordedByUserId = actor.UserId
        };
        db.ContextSessions.Add(session);
        await SetAttendeesAsync(session, actor, request.PersonIds, request.NewPeople, request.Confirm, reasons, ct);
        try
        {
            await scope.SaveAsync(ct);
        }
        catch (TrackerException) when (request.Id is not null)
        {
            // A retry may have raced the original request and lost on the primary key: answer as if it had found the
            // meeting. Any other failure is rethrown unchanged.
            if (await db.ContextSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == session.Id, ct) is not { } concurrent)
                throw;
            return (await ReplayAsync(concurrent, groupId, request, ct), false);
        }
        return (await ResponseAsync(session, ct), true);
    }

    /// <summary>
    /// Answers a create whose id is already recorded, as a retry of the request that recorded it. A retry must carry the
    /// same group, date and attendees; anything else reuses an id for a different meeting, or would silently drop the
    /// client's changes, so it is a Conflict. People the original request created have new ids, so only their number is
    /// compared.
    /// </summary>
    private async Task<SessionResponse> ReplayAsync(ContextSession recorded, Guid groupId, SessionRequest request, CancellationToken ct)
    {
        var listed = (request.PersonIds ?? []).Distinct().ToArray();
        var present = await db.Attendances.Where(a => a.SessionId == recorded.Id && a.RemovedAt == null)
            .Select(a => a.PersonId).ToArrayAsync(ct);
        if (recorded.ContextId != groupId || recorded.OccurredOn != request.OccurredOn
            || listed.Except(present).Any() || present.Length != listed.Length + (request.NewPeople?.Length ?? 0))
            throw Conflict();
        return await ResponseAsync(recorded, ct);
    }

    public async Task<SessionResponse> UpdateAttendeesAsync(Guid groupId, Guid sessionId, Guid expectedVersion, AttendeesRequest request, CancellationToken ct)
    {
        var (actor, _) = await scope.GroupAsync(groupId, ct);
        var session = await db.ContextSessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.ContextId == groupId, ct) ?? throw Missing();
        RequireVersion(session.Version, expectedVersion);
        RequireEditWindow(actor, session);
        await SetAttendeesAsync(session, actor, request.PersonIds, request.NewPeople, request.Confirm, [], ct);
        session.Version = Guid.NewGuid();
        await scope.SaveAsync(ct, TrackerFailure.PreconditionFailed);
        return await ResponseAsync(session, ct);
    }

    public async Task<PagedResponse<AttendanceRecordResponse>> GetRecordsAsync(Guid? personId, string? userId, PageRequest page, CancellationToken ct)
    {
        await scope.ActorAsync(ct);
        var records =
            from a in db.Attendances
            join s in db.ContextSessions on a.SessionId equals s.Id
            join p in db.People on a.PersonId equals p.Id
            where (personId == null || a.PersonId == personId)
                && (userId == null || a.RecordedByUserId == userId || a.RemovedByUserId == userId)
            // Newest event first: a removal counts from when it happened, not from when the row was first recorded.
            orderby a.RemovedAt ?? a.RecordedAt descending, a.Id descending
            select new AttendanceRecordResponse(s.Id, s.ContextId, s.OccurredOn, p.Id,
                p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName),
                a.RecordedByUserId, a.RecordedAt, a.RemovedByUserId, a.RemovedAt);
        return await records.PageAsync(page, ct);
    }

    /// <summary>
    /// Leaders may change a meeting only within <see cref="LeaderEditDays"/> days of when it was recorded, so late
    /// paperwork gets the same window as same-day entry; Staff at any time. Depends on the stored meeting, so it is
    /// checked here rather than on the endpoint.
    /// </summary>
    private void RequireEditWindow(ChurchActor actor, ContextSession session)
    {
        if (!TrackerScope.ManagesGroups(actor) && scope.Now > session.RecordedAt.AddDays(LeaderEditDays))
            throw Denied();
    }

    /// <summary>A meeting date must not be in the future and must fall while the group existed.</summary>
    private void RequireMeetingDate(Context group, DateOnly date)
    {
        var (start, end) = scope.Calendar.DayBounds(date);
        if (date > scope.Today || end <= group.StartsAt || (group.EndsAt is { } ended && start >= ended))
            throw Invalid();
    }

    /// <summary>
    /// Makes the meeting's present list match <paramref name="personIds"/> plus the people created from
    /// <paramref name="newPeople"/>. Existing rows for people still listed keep their original recorder; people left out
    /// are marked removed. Throws ConfirmationRequired, with nothing changed, when <paramref name="reasons"/> holds a
    /// warning or a new person's name matches someone who may already exist.
    /// </summary>
    private async Task SetAttendeesAsync(ContextSession session, ChurchActor actor, Guid[]? personIds, PersonRequest[]? newPeople,
        bool confirm, Dictionary<string, object?> reasons, CancellationToken ct)
    {
        var listed = (personIds ?? []).Distinct().ToArray();
        newPeople ??= [];
        if (listed.Length + newPeople.Length > MaxAttendees || newPeople.Length > MaxNewPeople)
            throw Invalid();

        var current = await db.Attendances.Where(a => a.SessionId == session.Id && a.RemovedAt == null).ToListAsync(ct);
        var added = listed.Except(current.Select(a => a.PersonId)).ToArray();
        // Someone newly marked present must be an active person in this church. Marking someone present gives the
        // group's Leaders their full profile for 90 days (TrackerScope.People), so a Leader could unlock anyone found by
        // name lookup. That is an accepted risk: it is detected, not prevented, through the recorder on each row and
        // the Staff attendance-records report.
        if (await db.People.CountAsync(p => added.Contains(p.Id) && p.IsActive, ct) != added.Length)
            throw Invalid();

        var now = scope.Now;
        // New people are always created active; a request cannot add someone who is already inactive.
        var created = newPeople.Select(request => request.IsActive == false
            ? throw Invalid()
            : PersonRecords.Create(request, actor, now)).ToArray();
        if (!confirm)
        {
            var similar = new List<SimilarPeopleResponse>();
            for (var i = 0; i < created.Length; i++)
            {
                if (created[i].Name.Length < PeopleService.LookupMinLength)
                    continue;
                var matches = await db.People.Where(p => p.IsActive).MatchingName(created[i].Name).ToLookupAsync(db, ct);
                if (matches.Length > 0)
                    similar.Add(new SimilarPeopleResponse(i, matches));
            }
            if (similar.Count > 0)
                reasons["similarPeople"] = similar;
        }
        if (reasons.Count > 0)
            throw ConfirmationRequired(reasons);

        db.People.AddRange(created);
        foreach (var personId in added.Concat(created.Select(p => p.Id)))
            db.Attendances.Add(new Attendance { SessionId = session.Id, PersonId = personId, RecordedAt = now, RecordedByUserId = actor.UserId });
        foreach (var removed in current.Where(a => !listed.Contains(a.PersonId)))
        {
            removed.RemovedAt = now;
            removed.RemovedByUserId = actor.UserId;
        }
    }

    /// <summary>
    /// Who came, and who was absent: active people who were members or leaders of the group at any point on the
    /// meeting's local date and are not marked present. Absence is worked out on read, never stored.
    /// </summary>
    private async Task<SessionResponse> ResponseAsync(ContextSession session, CancellationToken ct)
    {
        var (start, end) = scope.Calendar.DayBounds(session.OccurredOn);
        var present = db.Attendances.Where(a => a.SessionId == session.Id && a.RemovedAt == null).Select(a => a.PersonId);
        var attendees = await db.People.Where(p => present.Contains(p.Id))
            .OrderBy(p => p.FirstName).ThenBy(p => p.LastName).ThenBy(p => p.Id)
            .Select(NameOf).ToArrayAsync(ct);
        var absent = await db.People
            .Where(p => p.IsActive && !present.Contains(p.Id)
                && (db.ContextMembers.Any(m => m.ContextId == session.ContextId && m.PersonId == p.Id
                        && m.JoinedAt < end && (m.LeftAt == null || m.LeftAt > start))
                    || db.ContextLeaders.Any(l => l.ContextId == session.ContextId && l.PersonId == p.Id
                        && l.StartsAt < end && (l.EndsAt == null || l.EndsAt > start))))
            .OrderBy(p => p.FirstName).ThenBy(p => p.LastName).ThenBy(p => p.Id)
            .Select(NameOf).ToArrayAsync(ct);
        return new SessionResponse(session.Id, session.ContextId, session.OccurredOn, attendees, absent, session.Version);
    }
}
