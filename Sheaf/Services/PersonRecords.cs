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
/// someone is created (by Staff, or during attendance). Both return only <see cref="PersonLookupResponse"/>, never
/// profile details.
/// </summary>
internal static class PersonNameSearch
{
    /// <summary>Two-character terms match the start of a first or last name; longer terms match anywhere in the full name.</summary>
    public static IQueryable<Person> MatchingName(this IQueryable<Person> people, string term) =>
        term.Length < PeopleService.LookupContainsLength
            ? people.Where(p => p.FirstName.StartsWith(term) || (p.LastName != null && p.LastName.StartsWith(term)))
            : people.Where(p => (p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName)).Contains(term));

    /// <summary>
    /// The first <see cref="PeopleService.LookupMaxResults"/> matches in name order, with their household label and,
    /// for actors who manage every group, the groups they are in at <paramref name="now"/>.
    /// </summary>
    public static async Task<PersonLookupResponse[]> ToLookupAsync(this IQueryable<Person> matches, ApplicationDbContext db, ChurchActor actor, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await matches.Top()
            .Select(p => new
            {
                p.Id,
                Name = p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName),
                HouseholdLabel = db.Households.Where(h => h.Id == p.HouseholdId).Select(h => h.Label).FirstOrDefault(),
                InHousehold = p.HouseholdId != null
            })
            .ToArrayAsync(ct);
        var groups = await CurrentGroupsAsync(db, actor, [.. rows.Select(r => r.Id)], now, ct);
        return [.. rows.Select(r => new PersonLookupResponse(r.Id, r.Name, r.HouseholdLabel, GroupsOf(groups, r.Id), r.InHousehold))];
    }

    /// <summary>
    /// Names of the active groups each person is a member or leader of at <paramref name="now"/>, in name order, or null
    /// when the actor does not manage every group. Group membership can itself be sensitive ("Grief support"), so only
    /// those who already see every group get it; a Leader tells similar names apart by household label alone.
    /// </summary>
    private static async Task<Dictionary<Guid, string[]>?> CurrentGroupsAsync(ApplicationDbContext db, ChurchActor actor, Guid[] personIds, DateTimeOffset now, CancellationToken ct)
    {
        if (!TrackerScope.ManagesGroups(actor))
            return null;
        if (personIds.Length == 0)
            return [];
        // A person has one assignment per group at a time, so each current group appears once; the database orders the names.
        var rows = await (
            from a in db.GroupAssignments.CurrentAt(now)
            join g in db.Groups.ActiveAt(now) on a.GroupId equals g.Id
            where personIds.Contains(a.PersonId)
            orderby g.Name
            select new { a.PersonId, g.Name }).ToArrayAsync(ct);
        return rows.GroupBy(r => r.PersonId).ToDictionary(g => g.Key, g => g.Select(r => r.Name).ToArray());
    }

    /// <summary>The person's current group names, empty when they are in none, or null when the actor may not see groups.</summary>
    private static string[]? GroupsOf(Dictionary<Guid, string[]>? groups, Guid personId) =>
        groups is null ? null : groups.GetValueOrDefault(personId, []);

    /// <summary>
    /// Active people matching each name, in one query: for each name at least <see cref="PeopleService.LookupMinLength"/>
    /// long, the same matches <see cref="ToLookupAsync"/> would return for it, keyed by the name's index.
    /// </summary>
    public static async Task<SimilarPeopleResponse[]> SimilarAsync(ApplicationDbContext db, ChurchActor actor, IReadOnlyList<string> names, DateTimeOffset now, CancellationToken ct)
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
                    HouseholdLabel = db.Households.Where(h => h.Id == p.HouseholdId).Select(h => h.Label).FirstOrDefault(),
                    InHousehold = p.HouseholdId != null
                });
            query = query is null ? matches : query.Concat(matches);
        }
        if (query is null)
            return [];
        var rows = await query
            .OrderBy(m => m.Index).ThenBy(m => m.FirstName).ThenBy(m => m.LastName).ThenBy(m => m.Id)
            .ToArrayAsync(ct);
        var groups = await CurrentGroupsAsync(db, actor, [.. rows.Select(m => m.Id).Distinct()], now, ct);
        return [.. rows.GroupBy(m => m.Index).Select(g => new SimilarPeopleResponse(g.Key,
            [.. g.Select(m => new PersonLookupResponse(m.Id, m.LastName is null ? m.FirstName : $"{m.FirstName} {m.LastName}", m.HouseholdLabel,
                GroupsOf(groups, m.Id), m.InHousehold))]))];
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
        public bool InHousehold { get; init; }
    }
}
