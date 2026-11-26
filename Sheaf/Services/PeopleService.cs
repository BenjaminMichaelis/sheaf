using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

public interface IPeopleService
{
    Task<PersonResponse> CreatePersonAsync(PersonRequest request, CancellationToken ct);
    Task<PersonResponse> GetPersonAsync(Guid id, CancellationToken ct);
    Task<PersonResponse> UpdatePersonAsync(Guid id, Guid expectedVersion, PersonRequest request, CancellationToken ct);
    Task<PersonLookupResponse[]> LookupAsync(string? name, CancellationToken ct);
}

public sealed class PeopleService(ApplicationDbContext db, TrackerScope scope) : IPeopleService
{
    private static PersonResponse Profile(Person p) => new(p.Id, p.Name, p.FirstName, p.LastName, p.Email, p.Phone, p.HouseholdId, p.IsActive, p.Version);

    public async Task<PersonResponse> CreatePersonAsync(PersonRequest request, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        var person = PersonRecords.Create(request, actor, scope.Now);
        db.People.Add(person);
        await scope.SaveAsync(ct);
        return Profile(person);
    }

    public async Task<PersonResponse> GetPersonAsync(Guid id, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        return Profile(await scope.VisiblePeople(actor).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Missing());
    }

    public async Task<PersonResponse> UpdatePersonAsync(Guid id, Guid expectedVersion, PersonRequest request, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        var person = await scope.People(actor).SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Missing();
        RequireVersion(person.Version, expectedVersion);
        // Leaders may correct names and contacts; activating or deactivating a person is a Staff decision.
        // This depends on the stored value, so it is checked here rather than on the endpoint.
        if (request.IsActive is { } isActive && isActive != person.IsActive && !TrackerScope.ManagesPeople(actor))
            throw Denied();
        PersonRecords.Apply(person, request);
        person.Version = Guid.NewGuid();
        await scope.SaveAsync(ct, TrackerFailure.PreconditionFailed);
        return Profile(person);
    }

    /// <summary>Shortest accepted search. Two-character searches match only the start of a first or last name.</summary>
    public const int LookupMinLength = 2;
    /// <summary>Searches this long or longer match anywhere in the full name.</summary>
    public const int LookupContainsLength = 3;
    /// <summary>Most results one search returns. There is no paging, so the directory cannot be walked page by page.</summary>
    public const int LookupMaxResults = 20;

    public async Task<PersonLookupResponse[]> LookupAsync(string? name, CancellationToken ct)
    {
        // Any tracker user may find any active person by name, to record who attended. Only the name and the
        // household label come back; groups, contacts, and household members stay behind profile access.
        var term = Optional(name, 200);
        if (term is null || term.Length < LookupMinLength)
            throw Invalid();
        return await db.People.Where(p => p.IsActive).MatchingName(term).ToLookupAsync(db, ct);
    }
}
