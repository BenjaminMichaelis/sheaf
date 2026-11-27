using Microsoft.EntityFrameworkCore;

using Sheaf.Authorization;
using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

/// <summary>
/// Creates and edits <see cref="Person"/> rows the same way for every caller: Staff creating a profile and a Leader
/// adding someone while recording attendance get the same validation and audit fields.
/// </summary>
internal static class PersonRecords
{
    /// <summary>Builds a new active Person stamped with who created it. The caller adds it to the context and saves.</summary>
    public static Person Create(PersonRequest request, ChurchActor actor, DateTimeOffset now)
    {
        var person = new Person
        {
            FirstName = RequiredName(request.FirstName, 100),
            CreatedAt = now,
            CreatedByUserId = actor.UserId
        };
        Apply(person, request);
        return person;
    }

    /// <summary>Copies the request onto the person. An omitted IsActive keeps the current value.</summary>
    public static void Apply(Person person, PersonRequest request)
    {
        person.FirstName = RequiredName(request.FirstName, 100);
        person.LastName = Optional(request.LastName, 100);
        person.Email = Optional(request.Email, 320);
        person.Phone = Optional(request.Phone, 50);
        // An omitted IsActive keeps the current value, so a partial client cannot silently reactivate someone.
        person.IsActive = request.IsActive ?? person.IsActive;
    }
}

/// <summary>
/// The one definition of "people whose name matches", shared by name lookup and the possible-duplicate check when
/// someone is added during attendance. Both return only <see cref="PersonLookupResponse"/>, never profile details.
/// </summary>
internal static class PersonNameSearch
{
    /// <summary>Two-character terms match the start of a first or last name; longer terms match anywhere in the full name.</summary>
    public static IQueryable<Person> MatchingName(this IQueryable<Person> people, string term) =>
        term.Length < PeopleService.LookupContainsLength
            ? people.Where(p => p.FirstName.StartsWith(term) || (p.LastName != null && p.LastName.StartsWith(term)))
            : people.Where(p => (p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName)).Contains(term));

    /// <summary>The first <see cref="PeopleService.LookupMaxResults"/> matches in name order, with their household label.</summary>
    public static Task<PersonLookupResponse[]> ToLookupAsync(this IQueryable<Person> matches, ApplicationDbContext db, CancellationToken ct) =>
        matches.Top()
            .Select(p => new PersonLookupResponse(p.Id,
                p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName),
                db.Households.Where(h => h.Id == p.HouseholdId).Select(h => h.Label).FirstOrDefault()))
            .ToArrayAsync(ct);

    /// <summary>
    /// Active people matching each name, in one query: for each name at least <see cref="PeopleService.LookupMinLength"/>
    /// long, the same matches <see cref="ToLookupAsync"/> would return for it, keyed by the name's index.
    /// </summary>
    public static async Task<SimilarPeopleResponse[]> SimilarAsync(ApplicationDbContext db, IReadOnlyList<string> names, CancellationToken ct)
    {
        IQueryable<SimilarMatch>? query = null;
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i].Length < PeopleService.LookupMinLength)
                continue;
            var index = i;
            var matches = db.People.Where(p => p.IsActive).MatchingName(names[i]).Top()
                .Select(p => new SimilarMatch
                {
                    Index = index,
                    Id = p.Id,
                    FirstName = p.FirstName,
                    LastName = p.LastName,
                    HouseholdLabel = db.Households.Where(h => h.Id == p.HouseholdId).Select(h => h.Label).FirstOrDefault()
                });
            query = query is null ? matches : query.Concat(matches);
        }
        if (query is null)
            return [];
        var rows = await query
            .OrderBy(m => m.Index).ThenBy(m => m.FirstName).ThenBy(m => m.LastName).ThenBy(m => m.Id)
            .ToArrayAsync(ct);
        return [.. rows.GroupBy(m => m.Index).Select(g => new SimilarPeopleResponse(g.Key,
            [.. g.Select(m => new PersonLookupResponse(m.Id, m.LastName is null ? m.FirstName : $"{m.FirstName} {m.LastName}", m.HouseholdLabel))]))];
    }

    private static IQueryable<Person> Top(this IQueryable<Person> matches) =>
        matches.OrderBy(p => p.FirstName).ThenBy(p => p.LastName).ThenBy(p => p.Id).Take(PeopleService.LookupMaxResults);

    // An object initializer, not a constructor, so EF can translate the set operation and order its results.
    private sealed class SimilarMatch
    {
        public int Index { get; init; }
        public Guid Id { get; init; }
        public required string FirstName { get; init; }
        public string? LastName { get; init; }
        public string? HouseholdLabel { get; init; }
    }
}
