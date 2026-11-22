namespace Sheaf.Services;

/// <summary>Creates or updates a church-owned Person; account linking is not accepted.</summary>
public sealed record PersonRequest(string FirstName, string? LastName = null, string? Email = null, string? Phone = null, bool IsActive = true);
/// <summary>A scoped Person profile.</summary>
public sealed record PersonResponse(Guid Id, string Name, string FirstName, string? LastName, string? Email, string? Phone, Guid? HouseholdId, bool IsActive);
/// <summary>A limited same-church active Person lookup result.</summary>
public sealed record PersonLookupResponse(Guid Id, string Name, string[] CurrentGroups, string? HouseholdLabel, string[] HouseholdFirstNames);
/// <summary>A group type name.</summary>
public sealed record GroupTypeRequest(string Name);
/// <summary>A church-local group type.</summary>
public sealed record GroupTypeResponse(Guid Id, string Name);
/// <summary>A group with a required name and starting instant, and optional type.</summary>
public sealed record GroupRequest(string Name, DateTimeOffset StartsAt, Guid? GroupTypeId = null);
/// <summary>A retained group and its lifecycle.</summary>
public sealed record GroupResponse(Guid Id, string Name, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, Guid? GroupTypeId);
/// <summary>A dated group membership or leadership assignment.</summary>
public sealed record AssignmentRequest(Guid PersonId, DateTimeOffset StartsAt);
/// <summary>An instant for leaving an assignment or ending a group.</summary>
public sealed record EndRequest(DateTimeOffset EndsAt);
/// <summary>A retained membership or leadership interval.</summary>
public sealed record AssignmentResponse(Guid Id, Guid PersonId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt);
/// <summary>Household structure. An unspecified label preserves the existing label or defaults from the first submitted member's surname.</summary>
public sealed record HouseholdRequest(string? Label, Guid[] MemberIds);
/// <summary>A household with only member identifiers and first names.</summary>
public sealed record HouseholdResponse(Guid Id, string? Label, HouseholdMemberResponse[] Members);
/// <summary>A household member's limited identity.</summary>
public sealed record HouseholdMemberResponse(Guid Id, string FirstName);

public interface ITrackerService
{
    Task<PersonResponse> CreatePersonAsync(PersonRequest request, CancellationToken ct);
    Task<PersonResponse> GetPersonAsync(Guid id, CancellationToken ct);
    Task<PersonResponse> UpdatePersonAsync(Guid id, PersonRequest request, CancellationToken ct);
    Task<PersonLookupResponse[]> LookupAsync(string? name, CancellationToken ct);
    Task<GroupTypeResponse[]> GetTypesAsync(CancellationToken ct);
    Task<GroupTypeResponse> GetTypeAsync(Guid id, CancellationToken ct);
    Task<GroupTypeResponse> CreateTypeAsync(GroupTypeRequest request, CancellationToken ct);
    Task<GroupTypeResponse> UpdateTypeAsync(Guid id, GroupTypeRequest request, CancellationToken ct);
    Task<GroupResponse[]> GetGroupsAsync(CancellationToken ct);
    Task<GroupResponse> GetGroupAsync(Guid id, CancellationToken ct);
    Task<GroupResponse> CreateGroupAsync(GroupRequest request, CancellationToken ct);
    Task<GroupResponse> UpdateGroupAsync(Guid id, GroupRequest request, CancellationToken ct);
    Task<GroupResponse> EndGroupAsync(Guid id, EndRequest request, CancellationToken ct);
    Task<AssignmentResponse[]> GetAssignmentsAsync(Guid groupId, bool leaders, CancellationToken ct);
    Task<AssignmentResponse> GetAssignmentAsync(Guid groupId, Guid assignmentId, bool leaders, CancellationToken ct);
    Task<AssignmentResponse> AddAssignmentAsync(Guid groupId, AssignmentRequest request, bool leaders, CancellationToken ct);
    Task<AssignmentResponse> EndAssignmentAsync(Guid groupId, Guid assignmentId, EndRequest request, bool leaders, CancellationToken ct);
    Task<HouseholdResponse> GetHouseholdAsync(Guid id, CancellationToken ct);
    Task<HouseholdResponse> CreateHouseholdAsync(HouseholdRequest request, CancellationToken ct);
    Task<HouseholdResponse> UpdateHouseholdAsync(Guid id, HouseholdRequest request, CancellationToken ct);
}
