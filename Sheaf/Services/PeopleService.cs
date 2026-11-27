using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

public interface IPeopleService
{
    Task<PersonResponse> CreatePersonAsync(PersonRequest request, bool confirm, CancellationToken ct);
    Task<PersonResponse> GetPersonAsync(Guid id, CancellationToken ct);
    Task<PersonResponse> UpdatePersonAsync(Guid id, Guid expectedVersion, PersonRequest request, CancellationToken ct);
    Task<PersonLookupResponse[]> LookupAsync(string? name, CancellationToken ct);
}

public sealed class PeopleService(ApplicationDbContext db, TrackerScope scope) : IPeopleService
{
    /// <summary>
    /// The basic profile, with the household's label and the other members' first names. Every caller who can read a
    /// profile gets the same fields; <paramref name="canEdit"/> only tells the client whether it may offer changes.
    /// </summary>
    private async Task<PersonResponse> ProfileAsync(Person p, bool canEdit, CancellationToken ct)
    {
        var household = p.HouseholdId is not { } householdId ? null : await db.Households
            .Where(h => h.Id == householdId)
            .Select(h => new
            {
                h.Label,
                Members = db.People.Where(o => o.HouseholdId == h.Id && o.Id != p.Id)
                    .OrderBy(o => o.FirstName).ThenBy(o => o.Id)
                    .Select(o => new HouseholdMemberResponse(o.Id, o.FirstName)).ToArray()
            })
            .SingleOrDefaultAsync(ct);
        return new(p.Id, p.Name, p.FirstName, p.LastName, p.Email, p.Phone, p.HouseholdId, household?.Label,
            household?.Members ?? [], p.IsActive, canEdit, p.Version);
    }

    public async Task<PersonResponse> CreatePersonAsync(PersonRequest request, bool confirm, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        var person = PersonRecords.Create(request, actor, scope.Now);
        // The same possible-duplicate check as adding someone while recording attendance: a warning, never a block.
        if (!confirm && await PersonNameSearch.SimilarAsync(db, actor, [person.Name], scope.Now, ct) is { Length: > 0 } similar)
            throw ConfirmationRequired(new Dictionary<string, object?> { ["similarPeople"] = similar });
        db.People.Add(person);
        await scope.SaveAsync(ct);
        return await ProfileAsync(person, canEdit: true, ct);
    }

    public async Task<PersonResponse> GetPersonAsync(Guid id, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        var person = await scope.VisiblePeople(actor).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Missing();
        return await ProfileAsync(person, await scope.EditablePeople(actor).AnyAsync(p => p.Id == id, ct), ct);
    }

    public async Task<PersonResponse> UpdatePersonAsync(Guid id, Guid expectedVersion, PersonRequest request, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        var person = await scope.EditablePeople(actor).SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Missing();
        RequireVersion(person.Version, expectedVersion);
        // Leaders may correct names and contacts; activating or deactivating a person is a Staff decision.
        // This depends on the stored value, so it is checked here rather than on the endpoint.
        if (request.IsActive is { } isActive && isActive != person.IsActive && !TrackerScope.ManagesPeople(actor))
            throw Denied();
        PersonRecords.Apply(person, request);
        person.Version = Guid.NewGuid();
        await scope.SaveAsync(ct, TrackerFailure.PreconditionFailed);
        return await ProfileAsync(person, canEdit: true, ct);
    }

    /// <summary>Shortest accepted search. Two-character searches match only the start of a first or last name.</summary>
    public const int LookupMinLength = 2;
    /// <summary>Searches this long or longer match anywhere in the full name.</summary>
    public const int LookupContainsLength = 3;
    /// <summary>Most results one search returns. There is no paging, so the directory cannot be walked page by page.</summary>
    public const int LookupMaxResults = 20;

    public async Task<PersonLookupResponse[]> LookupAsync(string? name, CancellationToken ct)
    {
        // Any tracker user may find any active person by name, to record who attended. Only the name and household label
        // come back (plus current group names for Staff and Admins), enough to tell similar names apart; contacts and
        // household members stay behind profile access.
        var term = Optional(name, 200);
        if (term is null || term.Length < LookupMinLength)
            throw Invalid();
        var actor = await scope.ActorAsync(ct);
        return await db.People.Where(p => p.IsActive).MatchingName(term).ToLookupAsync(db, actor, scope.Now, ct);
    }
}
