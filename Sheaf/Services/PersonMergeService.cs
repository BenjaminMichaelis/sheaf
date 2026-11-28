using Microsoft.EntityFrameworkCore;

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
    /// emails compare without regard to case; phones compare digits only, ignoring spaces, dashes, dots, parentheses,
    /// and a leading plus.
    /// </summary>
    public async Task<PagedResponse<DuplicateCandidateResponse>> GetCandidatesAsync(PageRequest page, CancellationToken ct)
    {
        await scope.ActorAsync(ct);
        var candidates = db.People.Where(p => p.IsActive && p.MergedIntoId == null)
            .Select(p => new
            {
                Person = p,
                Digits = p.Phone == null ? null
                    : p.Phone.Replace(" ", "").Replace("-", "").Replace(".", "").Replace("(", "").Replace(")", "").Replace("+", "")
            });
        var pairs =
            from a in candidates
            from b in candidates
                // Each pair once, earlier record first: it is usually the original and the suggested survivor.
            where a.Person.CreatedAt < b.Person.CreatedAt
                || (a.Person.CreatedAt == b.Person.CreatedAt && a.Person.Id.CompareTo(b.Person.Id) < 0)
            let sameName = a.Person.FirstName == b.Person.FirstName && a.Person.LastName == b.Person.LastName
            let sameEmail = a.Person.Email != null && a.Person.Email == b.Person.Email
            let samePhone = a.Digits != null && a.Digits != "" && a.Digits == b.Digits
            where sameName || sameEmail || samePhone
            where !db.PersonDuplicateDismissals.Any(d => (d.PersonId == a.Person.Id && d.OtherPersonId == b.Person.Id)
                || (d.PersonId == b.Person.Id && d.OtherPersonId == a.Person.Id))
            orderby a.Person.FirstName, a.Person.LastName, a.Person.Id, b.Person.Id
            select new
            {
                First = new DuplicatePersonResponse(a.Person.Id,
                    a.Person.FirstName + (a.Person.LastName == null ? string.Empty : " " + a.Person.LastName),
                    a.Person.Email, a.Person.Phone,
                    db.Households.Where(h => h.Id == a.Person.HouseholdId).Select(h => h.Label).FirstOrDefault(),
                    a.Person.IsActive, a.Person.UserId != null, a.Person.CreatedAt),
                Second = new DuplicatePersonResponse(b.Person.Id,
                    b.Person.FirstName + (b.Person.LastName == null ? string.Empty : " " + b.Person.LastName),
                    b.Person.Email, b.Person.Phone,
                    db.Households.Where(h => h.Id == b.Person.HouseholdId).Select(h => h.Label).FirstOrDefault(),
                    b.Person.IsActive, b.Person.UserId != null, b.Person.CreatedAt),
                SameName = sameName,
                SameEmail = sameEmail,
                SamePhone = samePhone
            };
        var rows = await pairs.PageAsync(page, ct);
        return new([.. rows.Items.Select(r => new DuplicateCandidateResponse(r.First, r.Second, Reasons(r.SameName, r.SameEmail, r.SamePhone)))],
            rows.NextOffset);
    }

    private static DuplicateReason[] Reasons(bool name, bool email, bool phone) =>
    [
        .. name ? [DuplicateReason.Name] : Array.Empty<DuplicateReason>(),
        .. email ? [DuplicateReason.Email] : Array.Empty<DuplicateReason>(),
        .. phone ? [DuplicateReason.Phone] : Array.Empty<DuplicateReason>()
    ];

    /// <summary>Records that two people are different. Dismissing a pair again changes nothing.</summary>
    public async Task DismissAsync(DuplicateDismissalRequest request, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        if (request.PersonId == request.OtherPersonId)
            throw Invalid();
        var ids = new[] { request.PersonId, request.OtherPersonId };
        if (await scope.People(actor).CountAsync(p => ids.Contains(p.Id), ct) != 2)
            throw Missing();
        if (await db.PersonDuplicateDismissals.AnyAsync(d => (d.PersonId == request.PersonId && d.OtherPersonId == request.OtherPersonId)
                || (d.PersonId == request.OtherPersonId && d.OtherPersonId == request.PersonId), ct))
            return;
        var (low, high) = request.PersonId.CompareTo(request.OtherPersonId) < 0
            ? (request.PersonId, request.OtherPersonId)
            : (request.OtherPersonId, request.PersonId);
        db.PersonDuplicateDismissals.Add(new PersonDuplicateDismissal
        {
            PersonId = low,
            OtherPersonId = high,
            DismissedAt = scope.Now,
            DismissedByUserId = actor.UserId
        });
        await scope.SaveAsync(ct);
    }

    /// <summary>
    /// Merges the duplicate into the survivor in one transaction:
    /// <list type="bullet">
    /// <item>Fields follow <see cref="PersonMergeRequest.Fields"/>. The survivor is active if either was, and takes the
    /// duplicate's account link when it has none; if both have one, nothing is saved and the result is 409.</item>
    /// <item>Attendance moves to the survivor. Where both were present at the same meeting, the earlier record stays and
    /// the other is taken off the list, kept as history like any removal.</item>
    /// <item>Group assignments move to the survivor. Assignments in the same group whose intervals overlap become one,
    /// from the earliest start to the latest end, as Leader if any of them was.</item>
    /// <item>A household the merge leaves with nobody in it is deleted.</item>
    /// <item>The duplicate keeps its values, but becomes inactive, leaves its household and account, and records whom
    /// it was merged into, when, and by whom. People earlier merged into the duplicate now name the survivor.</item>
    /// </list>
    /// </summary>
    public async Task<PersonResponse> MergeAsync(Guid survivorId, Guid expectedVersion, PersonMergeRequest request, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        if (request.DuplicateId == survivorId)
            throw Invalid();
        var fields = request.Fields ?? new PersonMergeFields();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // A retry starts again from what the database holds now.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var pair = await scope.People(actor).Where(p => p.Id == survivorId || p.Id == request.DuplicateId).ToArrayAsync(ct);
            var survivor = pair.SingleOrDefault(p => p.Id == survivorId) ?? throw Missing();
            var duplicate = pair.SingleOrDefault(p => p.Id == request.DuplicateId) ?? throw Missing();
            RequireVersion(survivor.Version, expectedVersion);
            RequireVersion(duplicate.Version, request.DuplicateVersion);
            if (survivor.UserId != null && duplicate.UserId != null)
                throw new TrackerException(TrackerFailure.Conflict, new Dictionary<string, object?> { [ConflictReasonKey] = BothLinkedReason });

            var now = scope.Now;
            var ids = new[] { survivor.Id, duplicate.Id };
            var attendance = await db.Attendances.Where(a => ids.Contains(a.PersonId)).ToListAsync(ct);
            var assignments = await db.GroupAssignments.Where(a => ids.Contains(a.PersonId)).ToListAsync(ct);
            var survivorHousehold = survivor.HouseholdId;
            var duplicateHousehold = duplicate.HouseholdId;
            var userId = survivor.UserId ?? duplicate.UserId;
            var isActive = survivor.IsActive || duplicate.IsActive;

            // First save: everything that frees a unique slot (one open assignment per group, one active attendance per
            // meeting, one person per account) so the second save can move rows onto the survivor in any order.
            CollapseAttendance(attendance, actor.UserId, now);
            var extended = CollapseAssignments(assignments);
            duplicate.MergedIntoId = survivor.Id;
            duplicate.MergedAt = now;
            duplicate.MergedByUserId = actor.UserId;
            duplicate.IsActive = false;
            duplicate.UserId = null;
            duplicate.HouseholdId = null;
            duplicate.Version = Guid.NewGuid();
            await scope.SaveAsync(ct, TrackerFailure.PreconditionFailed);

            // Second save: extend and move what remains, and apply the chosen fields.
            foreach (var (assignment, startsAt, endsAt, role) in extended)
            {
                assignment.StartsAt = startsAt;
                assignment.EndsAt = endsAt;
                assignment.Role = role;
            }
            foreach (var a in attendance)
                a.PersonId = survivor.Id;
            foreach (var a in assignments)
                a.PersonId = survivor.Id;
            survivor.FirstName = fields.FirstName == PersonMergeChoice.Duplicate ? duplicate.FirstName : survivor.FirstName;
            survivor.LastName = Pick(fields.LastName, survivor.LastName, duplicate.LastName);
            survivor.Email = Pick(fields.Email, survivor.Email, duplicate.Email);
            survivor.Phone = Pick(fields.Phone, survivor.Phone, duplicate.Phone);
            survivor.HouseholdId = Pick(fields.Household, survivorHousehold, duplicateHousehold);
            survivor.IsActive = isActive;
            survivor.UserId = userId;
            survivor.Version = Guid.NewGuid();
            await foreach (var earlier in db.People.Where(p => p.MergedIntoId == duplicate.Id).AsAsyncEnumerable().WithCancellation(ct))
                earlier.MergedIntoId = survivor.Id;

            // Rosters and attendance lists changed, so their versions move and a concurrent edit of them is rejected.
            var groupIds = assignments.Select(a => a.GroupId).Distinct().ToArray();
            foreach (var group in await db.Groups.Where(g => groupIds.Contains(g.Id)).ToArrayAsync(ct))
                group.Version = Guid.NewGuid();
            var meetingIds = attendance.Select(a => a.MeetingId).Distinct().ToArray();
            foreach (var meeting in await db.GroupMeetings.Where(m => meetingIds.Contains(m.Id)).ToArrayAsync(ct))
                meeting.Version = Guid.NewGuid();
            await UpdateHouseholdsAsync([survivorHousehold, duplicateHousehold, survivor.HouseholdId], survivor, ct);

            await scope.SaveAsync(ct, TrackerFailure.PreconditionFailed);
            await transaction.CommitAsync(ct);
        });
        db.ChangeTracker.Clear();
        return await people.GetPersonAsync(survivorId, ct);
    }

    private static T? Pick<T>(PersonMergeChoice choice, T? survivor, T? duplicate) => choice switch
    {
        PersonMergeChoice.Survivor => survivor,
        PersonMergeChoice.Duplicate => duplicate,
        _ => survivor ?? duplicate
    };

    private static Guid? Pick(PersonMergeChoice choice, Guid? survivor, Guid? duplicate) => choice switch
    {
        PersonMergeChoice.Survivor => survivor,
        PersonMergeChoice.Duplicate => duplicate,
        _ => survivor ?? duplicate
    };

    /// <summary>
    /// Where both people are still on one meeting's list, keeps the earlier record and takes the other off, as the
    /// merging account.
    /// </summary>
    private static void CollapseAttendance(List<Attendance> attendance, string actorUserId, DateTimeOffset now)
    {
        foreach (var meeting in attendance.Where(a => a.RemovedAt == null).GroupBy(a => a.MeetingId).Where(g => g.Count() > 1))
        {
            foreach (var extra in meeting.OrderBy(a => a.RecordedAt).ThenBy(a => a.Id).Skip(1))
            {
                // A removal never precedes the record it removes, even if the clock is behind a recorded time.
                extra.RemovedAt = now < extra.RecordedAt ? extra.RecordedAt : now;
                extra.RemovedByUserId = actorUserId;
            }
        }
    }

    /// <summary>
    /// Joins overlapping assignments in the same group: the earliest-starting one is kept and widened (returned, to be
    /// applied after the others are deleted), and the rest are deleted from the context and from <paramref name="assignments"/>.
    /// Intervals that only touch stay separate, as they would for one person.
    /// </summary>
    private List<(GroupAssignment Assignment, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, GroupRole Role)> CollapseAssignments(List<GroupAssignment> assignments)
    {
        var widened = new List<(GroupAssignment, DateTimeOffset, DateTimeOffset?, GroupRole)>();
        var deleted = new List<GroupAssignment>();
        foreach (var group in assignments.GroupBy(a => a.GroupId))
        {
            GroupAssignment? keep = null;
            DateTimeOffset? end = null;
            var role = GroupRole.Member;
            var joined = false;
            foreach (var a in group.OrderBy(a => a.StartsAt).ThenBy(a => a.Id))
            {
                // Overlaps the interval so far: it starts before that interval ends (an open interval never ends).
                if (keep is not null && (end is null || a.StartsAt < end))
                {
                    end = end is null || a.EndsAt is null ? null : (a.EndsAt > end ? a.EndsAt : end);
                    role = a.Role == GroupRole.Leader ? GroupRole.Leader : role;
                    joined = true;
                    deleted.Add(a);
                    continue;
                }
                if (keep is not null && joined)
                    widened.Add((keep, keep.StartsAt, end, role));
                keep = a;
                end = a.EndsAt;
                role = a.Role;
                joined = false;
            }
            if (keep is not null && joined)
                widened.Add((keep, keep.StartsAt, end, role));
        }
        foreach (var a in deleted)
        {
            assignments.Remove(a);
            db.GroupAssignments.Remove(a);
        }
        return widened;
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
