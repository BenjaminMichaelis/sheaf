namespace Sheaf.Services;

/// <summary>A resource whose <see cref="Version"/> is its entity tag: send it back in If-Match to update it.</summary>
public interface IVersioned
{
    Guid Version { get; }
}

/// <summary>One page of results, in a stable order. <see cref="NextOffset"/> is null on the last page.</summary>
public sealed record PagedResponse<T>(T[] Items, int? NextOffset);

/// <summary>Creates or updates a church-owned Person; account linking is not accepted.</summary>
public sealed record PersonRequest(string FirstName, string? LastName = null, string? Email = null, string? Phone = null, bool? IsActive = null);
/// <summary>
/// A Person's basic profile: name, contacts, and household. Leaders also get this for people who came to their groups'
/// meetings recently (see TrackerScope.VisiblePeople), so anything more sensitive added to Person later must not be
/// returned here when <paramref name="CanEdit"/> is false. <paramref name="HouseholdMembers"/> lists the other people in
/// the household by first name only. <paramref name="CanEdit"/> says whether the caller may change this profile.
/// </summary>
public sealed record PersonResponse(Guid Id, string Name, string FirstName, string? LastName, string? Email, string? Phone, Guid? HouseholdId,
    string? HouseholdLabel, HouseholdMemberResponse[] HouseholdMembers, bool IsActive, bool CanEdit, Guid Version) : IVersioned;
/// <summary>
/// An active Person found by name: only enough to pick the right person, never profile details.
/// <paramref name="Groups"/> names the active groups they are in or lead today; it is null unless the caller manages
/// every group (Staff and Admins), because group membership can itself be sensitive.
/// </summary>
public sealed record PersonLookupResponse(Guid Id, string Name, string? HouseholdLabel, string[]? Groups);
/// <summary>A group type name. An omitted IsRetired keeps the current value.</summary>
public sealed record GroupTypeRequest(string Name, bool? IsRetired = null);
/// <summary>
/// A church-local group type. A retired type stays on groups that already have it but cannot be given to a group again.
/// </summary>
public sealed record GroupTypeResponse(Guid Id, string Name, bool IsRetired);
/// <summary>A group with a required name and starting instant, and optional type.</summary>
public sealed record GroupRequest(string Name, DateTimeOffset StartsAt, Guid? GroupTypeId = null);
/// <summary>A retained group and its lifecycle. Its version also changes when assignments are added or ended.</summary>
public sealed record GroupResponse(Guid Id, string Name, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, Guid? GroupTypeId, Guid Version) : IVersioned;
/// <summary>A dated group membership or leadership assignment.</summary>
public sealed record AssignmentRequest(Guid PersonId, DateTimeOffset StartsAt);
/// <summary>An instant for leaving an assignment or ending a group.</summary>
public sealed record EndRequest(DateTimeOffset EndsAt);
/// <summary>A retained membership or leadership interval.</summary>
public sealed record AssignmentResponse(Guid Id, Guid PersonId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt);
/// <summary>Household structure. Label may be omitted; an unspecified label preserves the existing label or defaults from the first submitted member's surname.</summary>
public sealed record HouseholdRequest(Guid[] MemberIds, string? Label = null);
/// <summary>A household with only member identifiers and first names.</summary>
public sealed record HouseholdResponse(Guid Id, string? Label, HouseholdMemberResponse[] Members, Guid Version) : IVersioned;
/// <summary>A household member's limited identity.</summary>
public sealed record HouseholdMemberResponse(Guid Id, string FirstName);
/// <summary>
/// Records a group meeting. <paramref name="PersonIds"/> are existing people who came; <paramref name="NewPeople"/> are
/// people to create and mark present in the same save. Possible duplicates (a similar name, another meeting that day)
/// are rejected with a confirmation problem unless <paramref name="Confirm"/> is true. <paramref name="Description"/> is an
/// optional note about the meeting, seen only by those who can see the group.
/// </summary>
public sealed record SessionRequest(DateOnly OccurredOn, Guid[]? PersonIds = null, PersonRequest[]? NewPeople = null, bool Confirm = false,
    string? Description = null);
/// <summary>
/// Replaces who came to a meeting and its description. <paramref name="PersonIds"/> is required: people left out are
/// taken off the list, which is kept as history, and an empty list takes everyone off. An omitted
/// <paramref name="Description"/> clears it.
/// </summary>
public sealed record AttendeesRequest(Guid[]? PersonIds, PersonRequest[]? NewPeople = null, bool Confirm = false, string? Description = null);
/// <summary>A person named on a meeting: who came, or a roster member who did not.</summary>
public sealed record SessionPersonResponse(Guid Id, string Name);
/// <summary>A meeting with its description, who came, and which roster members, as of that date, did not.</summary>
public sealed record SessionResponse(Guid Id, Guid GroupId, DateOnly OccurredOn, string? Description, SessionPersonResponse[] Attendees,
    SessionPersonResponse[] Absent, Guid Version) : IVersioned;
/// <summary>A meeting in a group's history.</summary>
public sealed record SessionSummaryResponse(Guid Id, DateOnly OccurredOn, int AttendeeCount, Guid Version) : IVersioned;
/// <summary>People who may already exist for the new person at <paramref name="Index"/> in the request (0 when creating one person).</summary>
public sealed record SimilarPeopleResponse(int Index, PersonLookupResponse[] Matches);
/// <summary>One person marked present, by whom, and whether and by whom they were taken off the list.</summary>
public sealed record AttendanceRecordResponse(Guid SessionId, Guid GroupId, DateOnly OccurredOn, Guid PersonId, string PersonName,
    string? RecordedByUserId, DateTimeOffset RecordedAt, string? RemovedByUserId, DateTimeOffset? RemovedAt);
