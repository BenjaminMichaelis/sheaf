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
    Task<SessionPersonResponse[]> GetRosterAsync(Guid groupId, DateOnly occurredOn, CancellationToken ct);
    Task<SessionResponse> CreateSessionAsync(Guid groupId, SessionRequest request, CancellationToken ct);
    Task<SessionResponse> UpdateAttendeesAsync(Guid groupId, Guid sessionId, Guid expectedVersion, AttendeesRequest request, CancellationToken ct);
    Task<PagedResponse<AttendanceRecordResponse>> GetRecordsAsync(Guid? personId, string? userId, PageRequest page, CancellationToken ct);
}

/// <summary>
/// Group meetings and who came. Anyone who can see a group records its meetings for any past date while the group
/// existed: Staff for every group, Leaders only for active groups they lead now, so once a group ends or a Leader steps
/// down, only Staff can record or change its meetings. Leaders may change a meeting for <see cref="LeaderEditDays"/>
/// days after it was recorded, Staff at any time.
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
        await scope.GroupAsync(groupId, GroupPermission.View, ct);
        return await db.GroupMeetings.Where(s => s.GroupId == groupId)
            .OrderByDescending(s => s.OccurredOn).ThenByDescending(s => s.RecordedAt).ThenByDescending(s => s.Id)
            .Select(s => new SessionSummaryResponse(s.Id, s.OccurredOn,
                db.Attendances.Count(a => a.MeetingId == s.Id && a.RemovedAt == null), s.Version))
            .PageAsync(page, ct);
    }

    public async Task<SessionResponse> GetSessionAsync(Guid groupId, Guid sessionId, CancellationToken ct)
    {
        var (actor, _) = await scope.GroupAsync(groupId, GroupPermission.View, ct);
        var session = await db.GroupMeetings.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.GroupId == groupId, ct) ?? throw Missing();
        // Seeing a group need not mean recording for it, so that is checked on its own, as UpdateAttendeesAsync does.
        var records = await scope.Groups(actor, GroupPermission.RecordAttendance).AnyAsync(g => g.Id == groupId, ct);
        return await ResponseAsync(session, records && InEditWindow(actor, session), ct);
    }

    public async Task<SessionPersonResponse[]> GetRosterAsync(Guid groupId, DateOnly occurredOn, CancellationToken ct)
    {
        var (_, group) = await scope.GroupAsync(groupId, GroupPermission.View, ct);
        // Only dates a meeting could be recorded for, so a future date cannot reveal who is due to join.
        RequireMeetingDate(group, occurredOn);
        return await ByName(RosterOn(groupId, occurredOn)).ToArrayAsync(ct);
    }

    public async Task<SessionResponse> CreateSessionAsync(Guid groupId, SessionRequest request, CancellationToken ct)
    {
        var (actor, group) = await scope.GroupAsync(groupId, GroupPermission.RecordAttendance, ct);
        // Any past date while the group existed may be recorded, so late paperwork can be caught up (by a Leader only
        // while they still lead the group). The Leader edit window applies only to changing a meeting once recorded.
        RequireMeetingDate(group, request.OccurredOn);

        // A second meeting the same day needs confirming, which also catches a double submit or a retried request.
        var reasons = new Dictionary<string, object?>();
        if (!request.Confirm)
        {
            var sameDay = await db.GroupMeetings.Where(s => s.GroupId == groupId && s.OccurredOn == request.OccurredOn)
                .OrderBy(s => s.RecordedAt).Select(s => s.Id).ToArrayAsync(ct);
            if (sameDay.Length > 0)
                reasons["existingSessionIds"] = sameDay;
        }
        var session = new GroupMeeting
        {
            GroupId = groupId,
            OccurredOn = request.OccurredOn,
            Description = Optional(request.Description, GroupMeeting.MaxDescriptionLength),
            RecordedAt = scope.Now,
            RecordedByUserId = actor.UserId
        };
        db.GroupMeetings.Add(session);
        await SetAttendeesAsync(session, actor, request.PersonIds, request.NewPeople, request.Confirm, reasons, ct);
        await scope.SaveAsync(ct);
        return await ResponseAsync(session, InEditWindow(actor, session), ct);
    }

    public async Task<SessionResponse> UpdateAttendeesAsync(Guid groupId, Guid sessionId, Guid expectedVersion, AttendeesRequest request, CancellationToken ct)
    {
        // The list replaces who came, so leaving it out would take everyone off; an empty list does that on purpose.
        if (request.PersonIds is null)
            throw Invalid();
        var (actor, _) = await scope.GroupAsync(groupId, GroupPermission.RecordAttendance, ct);
        var session = await db.GroupMeetings.SingleOrDefaultAsync(s => s.Id == sessionId && s.GroupId == groupId, ct) ?? throw Missing();
        RequireVersion(session.Version, expectedVersion);
        RequireEditWindow(actor, session);
        var description = Optional(request.Description, GroupMeeting.MaxDescriptionLength);
        await SetAttendeesAsync(session, actor, request.PersonIds, request.NewPeople, request.Confirm, [], ct);
        session.Description = description;
        session.Version = Guid.NewGuid();
        await scope.SaveAsync(ct, TrackerFailure.PreconditionFailed);
        return await ResponseAsync(session, InEditWindow(actor, session), ct);
    }

    public async Task<PagedResponse<AttendanceRecordResponse>> GetRecordsAsync(Guid? personId, string? userId, PageRequest page, CancellationToken ct)
    {
        await scope.ActorAsync(ct);
        var records =
            from a in db.Attendances
            join s in db.GroupMeetings on a.MeetingId equals s.Id
            join p in db.People on a.PersonId equals p.Id
            where (personId == null || a.PersonId == personId)
                && (userId == null || a.RecordedByUserId == userId || a.RemovedByUserId == userId)
            // Newest event first: a removal counts from when it happened, not from when the row was first recorded.
            orderby a.RemovedAt ?? a.RecordedAt descending, a.Id descending
            select new AttendanceRecordResponse(s.Id, s.GroupId, s.OccurredOn, p.Id,
                p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName),
                a.RecordedByUserId, a.RecordedAt, a.RemovedByUserId, a.RemovedAt);
        return await records.PageAsync(page, ct);
    }

    /// <summary>
    /// Leaders may change a meeting only within <see cref="LeaderEditDays"/> days of when it was recorded, so late
    /// paperwork gets the same window as same-day entry; Staff at any time. Depends on the stored meeting, so it is
    /// checked here rather than on the endpoint.
    /// </summary>
    private void RequireEditWindow(ChurchActor actor, GroupMeeting session)
    {
        if (!InEditWindow(actor, session))
            throw Denied();
    }

    private bool InEditWindow(ChurchActor actor, GroupMeeting session) =>
        TrackerScope.ManagesGroups(actor) || scope.Now <= session.RecordedAt.AddDays(LeaderEditDays);

    /// <summary>A meeting date must not be in the future and must fall while the group existed.</summary>
    private void RequireMeetingDate(Group group, DateOnly date)
    {
        // Compared as calendar dates first, so a date far outside the group's life is rejected before its day bounds
        // are worked out: those overflow at DateOnly's extremes.
        if (date > scope.Today || date < scope.Calendar.DateOf(group.StartsAt))
            throw Invalid();
        var (start, _) = scope.Calendar.DayBounds(date);
        if (group.EndsAt is { } ended && start >= ended)
            throw Invalid();
    }

    /// <summary>
    /// Makes the meeting's present list match <paramref name="personIds"/> plus the people created from
    /// <paramref name="newPeople"/>. Existing rows for people still listed keep their original recorder; people left out
    /// are marked removed. Throws ConfirmationRequired, with nothing changed, when <paramref name="reasons"/> holds a
    /// warning or a new person's name matches someone who may already exist.
    /// </summary>
    private async Task SetAttendeesAsync(GroupMeeting session, ChurchActor actor, Guid[]? personIds, PersonRequest[]? newPeople,
        bool confirm, Dictionary<string, object?> reasons, CancellationToken ct)
    {
        var listed = (personIds ?? []).Distinct().ToArray();
        newPeople ??= [];
        if (listed.Length + newPeople.Length > MaxAttendees || newPeople.Length > MaxNewPeople)
            throw Invalid();

        var current = await db.Attendances.Where(a => a.MeetingId == session.Id && a.RemovedAt == null).ToListAsync(ct);
        var added = listed.Except(current.Select(a => a.PersonId)).ToArray();
        // Someone newly marked present must be an active person in this church. Marking someone present lets the
        // group's Leaders read their basic profile for 90 days (TrackerScope.VisiblePeople), so a Leader could unlock
        // read access to anyone found by name lookup; it never grants edit, roster, or household access. That is an
        // accepted risk: it is detected, not prevented, through the recorder on each row and the Staff
        // attendance-records report.
        if (await db.People.CountAsync(p => added.Contains(p.Id) && p.IsActive, ct) != added.Length)
            throw Invalid();

        var now = scope.Now;
        // New people are always created active; a request cannot add someone who is already inactive.
        var created = newPeople.Select(request => request.IsActive == false
            ? throw Invalid()
            : PersonRecords.Create(request, actor, now)).ToArray();
        if (!confirm && await PersonNameSearch.SimilarAsync(db, actor, [.. created.Select(p => p.Name)], now, ct) is { Length: > 0 } similar)
            reasons["similarPeople"] = similar;
        if (reasons.Count > 0)
            throw ConfirmationRequired(reasons);

        db.People.AddRange(created);
        foreach (var personId in added.Concat(created.Select(p => p.Id)))
            db.Attendances.Add(new Attendance { MeetingId = session.Id, PersonId = personId, RecordedAt = now, RecordedByUserId = actor.UserId });
        foreach (var removed in current.Where(a => !listed.Contains(a.PersonId)))
        {
            removed.RemovedAt = now;
            removed.RemovedByUserId = actor.UserId;
        }
    }

    /// <summary>Active people on the group's roster, in any role, at any point on <paramref name="date"/> in the church's time zone.</summary>
    private IQueryable<Person> RosterOn(Guid groupId, DateOnly date)
    {
        var (start, end) = scope.Calendar.DayBounds(date);
        return db.People.Where(p => p.IsActive && db.GroupAssignments.Any(a => a.GroupId == groupId && a.PersonId == p.Id
            && a.StartsAt < end && (a.EndsAt == null || a.EndsAt > start)));
    }

    private static IQueryable<SessionPersonResponse> ByName(IQueryable<Person> people) =>
        people.OrderBy(p => p.FirstName).ThenBy(p => p.LastName).ThenBy(p => p.Id).Select(NameOf);

    /// <summary>Who came, and who was absent: people on the roster that day (<see cref="RosterOn"/>) not marked present. Absence is worked out on read, never stored.</summary>
    private async Task<SessionResponse> ResponseAsync(GroupMeeting session, bool canEdit, CancellationToken ct)
    {
        var present = db.Attendances.Where(a => a.MeetingId == session.Id && a.RemovedAt == null).Select(a => a.PersonId);
        var attendees = await ByName(db.People.Where(p => present.Contains(p.Id))).ToArrayAsync(ct);
        var absent = await ByName(RosterOn(session.GroupId, session.OccurredOn).Where(p => !present.Contains(p.Id))).ToArrayAsync(ct);
        return new SessionResponse(session.Id, session.GroupId, session.OccurredOn, session.Description, attendees, absent, canEdit, session.Version);
    }
}
