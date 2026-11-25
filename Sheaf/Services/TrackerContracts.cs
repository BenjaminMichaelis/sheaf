namespace Sheaf.Services;

/// <summary>A resource whose <see cref="Version"/> is its entity tag: send it back in If-Match to update it.</summary>
public interface IVersioned
{
    Guid Version { get; }
}

/// <summary>One page of results, in a stable order. <see cref="NextOffset"/> is null on the last page.</summary>
public sealed record PagedResponse<T>(T[] Items, int? NextOffset);

/// <summary>Creates or updates a church-owned Person; account linking is not accepted.</summary>
public sealed record PersonRequest(string FirstName, string? LastName = null, string? Email = null, string? Phone = null, bool IsActive = true);
/// <summary>A scoped Person profile.</summary>
public sealed record PersonResponse(Guid Id, string Name, string FirstName, string? LastName, string? Email, string? Phone, Guid? HouseholdId, bool IsActive, Guid Version) : IVersioned;
/// <summary>A limited same-church active Person lookup result.</summary>
public sealed record PersonLookupResponse(Guid Id, string Name, string[] CurrentGroups, string? HouseholdLabel, string[] HouseholdFirstNames);
/// <summary>A group type name.</summary>
public sealed record GroupTypeRequest(string Name);
/// <summary>A church-local group type.</summary>
public sealed record GroupTypeResponse(Guid Id, string Name);
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
