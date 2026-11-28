using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

/// <summary>
/// Finds and merges duplicate people. Every guest becomes a Person, so the same human is often entered twice; Staff and
/// Admins (<see cref="Sheaf.Authorization.Permission.PeopleManage"/>) review suggested pairs and merge them. A merge is
/// final: everything that pointed at the duplicate points at the survivor, and the duplicate stays as an inactive,
/// read-only record of what it was.
/// </summary>
public interface IPersonMergeService
{
    Task<PagedResponse<DuplicateCandidateResponse>> GetCandidatesAsync(PageRequest page, CancellationToken ct);
    Task DismissAsync(DuplicateDismissalRequest request, CancellationToken ct);
    Task<PersonResponse> MergeAsync(Guid survivorId, Guid expectedVersion, PersonMergeRequest request, CancellationToken ct);
}

public sealed class PersonMergeService(ApplicationDbContext db, TrackerScope scope, IPeopleService people) : IPersonMergeService
{
    /// <summary>The problem member that says why a merge was refused with 409, beyond a concurrent change.</summary>
    public const string ConflictReasonKey = "reason";
    /// <summary>Both people sign in with their own account, and a person can be linked to only one.</summary>
    public const string BothLinkedReason = "bothLinkedToAccounts";

    /// <summary>
    /// Pairs of active, unmerged people with the same full name, email, or phone, minus pairs Staff dismissed. Names and
    /// emails compare with the database collation, which is case-insensitive by default on SQL Server; phones compare
    /// digits only, ignoring spaces, dashes, dots, parentheses, and a leading plus. Each kind of match is an equality join,
    /// so the database can use hash joins instead of comparing every pair of people.
    /// </summary>
    public async Task<PagedResponse<DuplicateCandidateResponse>> GetCandidatesAsync(PageRequest page, CancellationToken ct)
    {
        await scope.ActorAsync(ct);
        var active = db.People.Where(p => p.IsActive && p.MergedIntoId == null);
        var phones = active.Where(p => p.Phone != null).Select(p => new
        {
            p.Id,
            p.CreatedAt,
            // Phone is never null here; the fallback only satisfies nullable flow analysis instead of using "!".
            Digits = (p.Phone ?? "").Replace(" ", "").Replace("-", "").Replace(".", "").Replace("(", "").Replace(")", "").Replace("+", "")
        }).Where(p => p.Digits != "");
        // Each pair once, earlier record first: it is usually the original and the suggested survivor.
        var byName =
            from a in active
            join b in active on new { a.FirstName, LastName = a.LastName ?? "" } equals new { b.FirstName, LastName = b.LastName ?? "" }
            where a.CreatedAt < b.CreatedAt || (a.CreatedAt == b.CreatedAt && a.Id.CompareTo(b.Id) < 0)
            select new MatchRow { PersonId = a.Id, OtherId = b.Id, Name = 1, Email = 0, Phone = 0 };
        var byEmail =
            from a in active.Where(p => p.Email != null)
            join b in active on a.Email equals b.Email
            where a.CreatedAt < b.CreatedAt || (a.CreatedAt == b.CreatedAt && a.Id.CompareTo(b.Id) < 0)
            select new MatchRow { PersonId = a.Id, OtherId = b.Id, Name = 0, Email = 1, Phone = 0 };
        var byPhone =
            from a in phones
            join b in phones on a.Digits equals b.Digits
            where a.CreatedAt < b.CreatedAt || (a.CreatedAt == b.CreatedAt && a.Id.CompareTo(b.Id) < 0)
            select new MatchRow { PersonId = a.Id, OtherId = b.Id, Name = 0, Email = 0, Phone = 1 };
        var matches = byName.Concat(byEmail).Concat(byPhone)
            .GroupBy(m => new { m.PersonId, m.OtherId })
            .Select(g => new { g.Key.PersonId, g.Key.OtherId, Name = g.Max(m => m.Name), Email = g.Max(m => m.Email), Phone = g.Max(m => m.Phone) })
            .Where(m => !db.PersonDuplicateDismissals.Any(d => (d.PersonId == m.PersonId && d.OtherPersonId == m.OtherId)
                || (d.PersonId == m.OtherId && d.OtherPersonId == m.PersonId)));
        var pairs =
            from m in matches
            join a in db.People on m.PersonId equals a.Id
            join b in db.People on m.OtherId equals b.Id
            orderby a.FirstName, a.LastName, a.Id, b.Id
            select new
            {
                Person = new DuplicatePersonResponse(a.Id, a.FirstName + (a.LastName == null ? string.Empty : " " + a.LastName),
                    a.Email, a.Phone, db.Households.Where(h => h.Id == a.HouseholdId).Select(h => h.Label).FirstOrDefault(),
                    a.UserId != null, a.CreatedAt),
                Other = new DuplicatePersonResponse(b.Id, b.FirstName + (b.LastName == null ? string.Empty : " " + b.LastName),
                    b.Email, b.Phone, db.Households.Where(h => h.Id == b.HouseholdId).Select(h => h.Label).FirstOrDefault(),
                    b.UserId != null, b.CreatedAt),
                m.Name,
                m.Email,
                m.Phone
            };
        var rows = await pairs.PageAsync(page, ct);
        return new([.. rows.Items.Select(r => new DuplicateCandidateResponse(r.Person, r.Other, Reasons(r.Name, r.Email, r.Phone)))],
            rows.NextOffset);
    }

    private static DuplicateReason[] Reasons(int name, int email, int phone)
    {
        var reasons = new List<DuplicateReason>();
        if (name > 0)
            reasons.Add(DuplicateReason.Name);
        if (email > 0)
            reasons.Add(DuplicateReason.Email);
        if (phone > 0)
            reasons.Add(DuplicateReason.Phone);
        return [.. reasons];
    }

    // An object initializer, not an anonymous type, so EF can translate the set operation over the three kinds of match.
    private sealed class MatchRow
    {
        public Guid PersonId { get; init; }
        public Guid OtherId { get; init; }
        public int Name { get; init; }
        public int Email { get; init; }
        public int Phone { get; init; }
    }

    /// <summary>Records that two people are different. Dismissing a pair again, even at the same moment, changes nothing.</summary>
    public async Task DismissAsync(DuplicateDismissalRequest request, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        if (request.PersonId == request.OtherPersonId)
            throw Invalid();
        var ids = new[] { request.PersonId, request.OtherPersonId };
        if (await scope.People(actor).CountAsync(p => ids.Contains(p.Id), ct) != 2)
            throw Missing();
        var (low, high) = Ordered(request.PersonId, request.OtherPersonId);
        if (await DismissedAsync(low, high, ct))
            return;
        db.PersonDuplicateDismissals.Add(new PersonDuplicateDismissal
        {
            PersonId = low,
            OtherPersonId = high,
            DismissedAt = scope.Now,
            DismissedByUserId = actor.UserId
        });
        try
        {
            await scope.SaveAsync(ct);
        }
        catch (TrackerException)
        {
            // The only failure expected here is the unique-index race with a concurrent dismissal of the same pair, whose
            // outcome is the one asked for; anything else is rethrown.
            db.ChangeTracker.Clear();
            if (!await DismissedAsync(low, high, ct))
                throw;
        }
    }

    private Task<bool> DismissedAsync(Guid low, Guid high, CancellationToken ct) =>
        db.PersonDuplicateDismissals.AnyAsync(d => d.PersonId == low && d.OtherPersonId == high, ct);

    /// <summary>A dismissed pair is stored lower identifier first, so each pair has one row.</summary>
    private static (Guid Low, Guid High) Ordered(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);

    /// <summary>
    /// Merges the duplicate into the survivor in one transaction:
    /// <list type="bullet">
    /// <item>Fields follow <see cref="PersonMergeRequest.Fields"/>. The survivor is active if either was, and takes the
    /// duplicate's account link when it has none; if both have one, nothing is saved and the result is 409.</item>
    /// <item>Attendance moves to the survivor. Where both were present at the same meeting, the earlier record stays and
    /// the other is taken off the list, kept as history like any removal.</item>
    /// <item>Group assignments move to the survivor. Assignments in the same group whose intervals overlap become one,
    /// from the earliest start. Across roles Leader wins, but only as long as the leadership lasted, so a past leadership
    /// never becomes current access (see <see cref="JoinAssignments"/>).</item>
    /// <item>Dismissed pairs that named the duplicate name the survivor.</item>
    /// <item>A household the merge leaves with nobody in it is deleted.</item>
    /// <item>The duplicate keeps its values, but becomes inactive, leaves its household and account, and records whom
    /// it was merged into, when, and by whom. People earlier merged into the duplicate now name the survivor.</item>
    /// </list>
    /// </summary>
    public async Task<PersonResponse> MergeAsync(Guid survivorId, Guid expectedVersion, PersonMergeRequest request, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        var fields = request.Fields ?? new PersonMergeFields();
        if (request.DuplicateId == survivorId
            || !Enum.IsDefined(fields.FirstName) || !Enum.IsDefined(fields.LastName) || !Enum.IsDefined(fields.Email)
            || !Enum.IsDefined(fields.Phone) || !Enum.IsDefined(fields.Household))
            throw Invalid();
        var strategy = db.Database.CreateExecutionStrategy();
        // A retry after a commit whose acknowledgement was lost checks whether the merge already happened instead of
        // running again (https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency).
        await strategy.ExecuteInTransactionAsync(
            async cancellation => await MergeInTransactionAsync(actor, survivorId, expectedVersion, request, fields, cancellation),
            cancellation => db.People.AsNoTracking().AnyAsync(p => p.Id == request.DuplicateId && p.MergedIntoId == survivorId, cancellation),
            ct);
        return await people.GetPersonAsync(survivorId, ct);
    }

    private async Task MergeInTransactionAsync(ChurchActor actor, Guid survivorId, Guid expectedVersion, PersonMergeRequest request,
        PersonMergeFields fields, CancellationToken ct)
    {
        // A retry starts again from what the database holds now.
        db.ChangeTracker.Clear();
        var pair = await scope.People(actor).Where(p => p.Id == survivorId || p.Id == request.DuplicateId).ToArrayAsync(ct);
        var survivor = pair.SingleOrDefault(p => p.Id == survivorId) ?? throw Missing();
        var duplicate = pair.SingleOrDefault(p => p.Id == request.DuplicateId) ?? throw Missing();
        RequireVersion(survivor.Version, expectedVersion);
        RequireVersion(duplicate.Version, request.DuplicateVersion);
        if (survivor.UserId != null && duplicate.UserId != null)
            throw new TrackerException(TrackerFailure.Conflict, new Dictionary<string, object?> { [ConflictReasonKey] = BothLinkedReason });

        var ids = new[] { survivor.Id, duplicate.Id };
        var assignments = await db.GroupAssignments.Where(a => ids.Contains(a.PersonId)).ToListAsync(ct);
        var joined = JoinAssignments(assignments);
        var attendance = await db.Attendances.Where(a => ids.Contains(a.PersonId)).ToListAsync(ct);
        var now = scope.Now;
        var survivorHousehold = survivor.HouseholdId;
        var duplicateHousehold = duplicate.HouseholdId;
        var userId = survivor.UserId ?? duplicate.UserId;
        var isActive = survivor.IsActive || duplicate.IsActive;

        // First save: everything that frees a unique slot (one open assignment per group, one active attendance per
        // meeting, one person per account), because EF cannot order writes by a filtered index. The second save can
        // then move rows onto the survivor in any order.
        RemoveSharedAttendance(attendance, actor.UserId, now);
        db.GroupAssignments.RemoveRange(joined.Absorbed);
        // A change that leaves an interval closed takes no open slot, so it is safe now; one that opens an interval waits
        // until the absorbed open rows are gone.
        foreach (var change in joined.Changes.Where(c => c.EndsAt is not null))
            change.Apply();
        duplicate.MergedIntoId = survivor.Id;
        duplicate.MergedAt = now;
        duplicate.MergedByUserId = actor.UserId;
        duplicate.IsActive = false;
        duplicate.UserId = null;
        duplicate.HouseholdId = null;
        duplicate.Version = Guid.NewGuid();
        await scope.SaveAsync(ct, TrackerFailure.PreconditionFailed);

        // Second save: widen and move what remains, and apply the chosen fields.
        foreach (var change in joined.Changes.Where(c => c.EndsAt is null))
            change.Apply();
        foreach (var a in attendance)
            a.PersonId = survivor.Id;
        foreach (var a in assignments.Except(joined.Absorbed))
            a.PersonId = survivor.Id;
        survivor.FirstName = fields.FirstName == PersonMergeChoice.Duplicate ? duplicate.FirstName : survivor.FirstName;
        survivor.LastName = Pick(fields.LastName, survivor.LastName, duplicate.LastName);
        survivor.Email = Pick(fields.Email, survivor.Email, duplicate.Email);
        survivor.Phone = Pick(fields.Phone, survivor.Phone, duplicate.Phone);
        survivor.HouseholdId = Pick(fields.Household, survivorHousehold, duplicateHousehold);
        survivor.IsActive = isActive;
        survivor.UserId = userId;
        survivor.Version = Guid.NewGuid();
        foreach (var earlier in await db.People.Where(p => p.MergedIntoId == duplicate.Id).ToArrayAsync(ct))
            earlier.MergedIntoId = survivor.Id;
        await MoveDismissalsAsync(duplicate.Id, survivor.Id, ct);

        // Rosters and attendance lists changed, so their versions move and a concurrent edit of them is rejected.
        var groupIds = assignments.Select(a => a.GroupId).Distinct().ToArray();
        foreach (var group in await db.Groups.Where(g => groupIds.Contains(g.Id)).ToArrayAsync(ct))
            group.Version = Guid.NewGuid();
        var meetingIds = attendance.Select(a => a.MeetingId).Distinct().ToArray();
        foreach (var meeting in await db.GroupMeetings.Where(m => meetingIds.Contains(m.Id)).ToArrayAsync(ct))
            meeting.Version = Guid.NewGuid();
        await UpdateHouseholdsAsync([survivorHousehold, duplicateHousehold, survivor.HouseholdId], survivor, ct);

        await scope.SaveAsync(ct, TrackerFailure.PreconditionFailed);
    }

    private static T Pick<T>(PersonMergeChoice choice, T survivor, T duplicate) => choice switch
    {
        PersonMergeChoice.Survivor => survivor,
        PersonMergeChoice.Duplicate => duplicate,
        _ => survivor ?? duplicate
    };

    /// <summary>
    /// Where both people are still on one meeting's list, keeps the earlier record and takes the other off, as the
    /// merging account.
    /// </summary>
    private static void RemoveSharedAttendance(List<Attendance> attendance, string actorUserId, DateTimeOffset now)
    {
        foreach (var meeting in attendance.Where(a => a.RemovedAt == null).GroupBy(a => a.MeetingId))
        {
            foreach (var extra in meeting.OrderBy(a => a.RecordedAt).ThenBy(a => a.Id).Skip(1))
            {
                extra.RemovedAt = now;
                extra.RemovedByUserId = actorUserId;
            }
        }
    }

    /// <summary>A kept assignment's new interval and role.</summary>
    private readonly record struct AssignmentChange(GroupAssignment Assignment, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, GroupRole Role)
    {
        public void Apply()
        {
            Assignment.StartsAt = StartsAt;
            Assignment.EndsAt = EndsAt;
            Assignment.Role = Role;
        }
    }

    private sealed record JoinedAssignments(List<GroupAssignment> Absorbed, List<AssignmentChange> Changes);

    /// <summary>
    /// Plans how two people's assignments become one person's. In each group, sorted by start, an assignment that begins
    /// before the current run ends (an open run never ends) joins that run; intervals that only touch stay separate, as a
    /// role change leaves them. Each run of two or more becomes one interval from its earliest start:
    /// <list type="bullet">
    /// <item>All one role: that role, to the run's latest end.</item>
    /// <item>Mixed roles, Leader wins: Leader, but never longer than the leadership itself. If a Leader interval is still
    /// open, the joined Leader interval is open. Otherwise it ends when the last Leader interval did, and any Member time
    /// after that stays a separate Member interval, so a past leadership never becomes current access.</item>
    /// </list>
    /// The run's first assignment and, when needed, one more are kept and changed; the rest are absorbed (deleted).
    /// </summary>
    private static JoinedAssignments JoinAssignments(List<GroupAssignment> assignments)
    {
        var absorbed = new List<GroupAssignment>();
        var changes = new List<AssignmentChange>();
        foreach (var group in assignments.GroupBy(a => a.GroupId))
        {
            var run = new List<GroupAssignment>();
            DateTimeOffset? runEnd = null;
            foreach (var a in group.OrderBy(a => a.StartsAt).ThenBy(a => a.Id))
            {
                if (run.Count > 0 && runEnd is { } end && a.StartsAt >= end)
                {
                    Plan(run, runEnd, absorbed, changes);
                    run = [];
                }
                runEnd = run.Count == 0 ? a.EndsAt : Later(runEnd, a.EndsAt);
                run.Add(a);
            }
            Plan(run, runEnd, absorbed, changes);
        }
        return new(absorbed, changes);
    }

    private static void Plan(List<GroupAssignment> run, DateTimeOffset? runEnd, List<GroupAssignment> absorbed, List<AssignmentChange> changes)
    {
        if (run.Count < 2)
            return;
        var first = run[0];
        var leaders = run.Where(a => a.Role == GroupRole.Leader).ToArray();
        if (leaders.Length == 0 || leaders.Length == run.Count)
        {
            changes.Add(new(first, first.StartsAt, runEnd, first.Role));
            absorbed.AddRange(run.Skip(1));
            return;
        }
        var leaderEnd = leaders.Select(a => a.EndsAt).Aggregate(Later);
        changes.Add(new(first, first.StartsAt, leaderEnd, GroupRole.Leader));
        // Members stayed on after the last leadership ended: that time is kept as Member, starting where Leader ended.
        if (leaderEnd is { } ended && (runEnd is null || runEnd > ended))
        {
            changes.Add(new(run[1], ended, runEnd, GroupRole.Member));
            absorbed.AddRange(run.Skip(2));
        }
        else
            absorbed.AddRange(run.Skip(1));
    }

    /// <summary>The later of two ends, where null (still open) is latest.</summary>
    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null || b is null ? null : (a > b ? a : b);

    /// <summary>
    /// Moves "not a duplicate" decisions from the duplicate to the survivor. A decision about the merged pair itself, or
    /// one the survivor already has, is dropped.
    /// </summary>
    private async Task MoveDismissalsAsync(Guid duplicateId, Guid survivorId, CancellationToken ct)
    {
        var moving = await db.PersonDuplicateDismissals
            .Where(d => d.PersonId == duplicateId || d.OtherPersonId == duplicateId).ToArrayAsync(ct);
        var existing = (await db.PersonDuplicateDismissals
            .Where(d => d.PersonId == survivorId || d.OtherPersonId == survivorId)
            .Select(d => new { d.PersonId, d.OtherPersonId }).ToArrayAsync(ct))
            .Select(d => Ordered(d.PersonId, d.OtherPersonId)).ToHashSet();
        foreach (var dismissal in moving)
        {
            var other = dismissal.PersonId == duplicateId ? dismissal.OtherPersonId : dismissal.PersonId;
            var pair = Ordered(survivorId, other);
            if (other == survivorId || !existing.Add(pair))
            {
                db.PersonDuplicateDismissals.Remove(dismissal);
                continue;
            }
            (dismissal.PersonId, dismissal.OtherPersonId) = pair;
        }
    }

    /// <summary>Deletes households the merge left empty and moves the version of the rest.</summary>
    private async Task UpdateHouseholdsAsync(Guid?[] touched, Person survivor, CancellationToken ct)
    {
        var ids = touched.OfType<Guid>().Distinct().ToArray();
        foreach (var household in await db.Households.Where(h => ids.Contains(h.Id)).ToArrayAsync(ct))
        {
            // The survivor's new household is saved only on the next save, so it is checked in memory.
            var occupied = survivor.HouseholdId == household.Id
                || await db.People.AnyAsync(p => p.HouseholdId == household.Id && p.Id != survivor.Id, ct);
            if (occupied)
                household.Version = Guid.NewGuid();
            else
                db.Households.Remove(household);
        }
    }
}
