using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

using static Sheaf.Services.TrackerRules;

namespace Sheaf.Services;

public interface IPeopleService
{
    Task<PersonResponse> CreatePersonAsync(PersonRequest request, CancellationToken ct);
    Task<PersonResponse> GetPersonAsync(Guid id, CancellationToken ct);
    Task<PersonResponse> UpdatePersonAsync(Guid id, PersonRequest request, CancellationToken ct);
    Task<PersonLookupResponse[]> LookupAsync(string? name, CancellationToken ct);
}

public sealed class PeopleService(ApplicationDbContext db, TrackerScope scope) : IPeopleService
{
    private static void SetPerson(Person person, PersonRequest request)
    {
        person.FirstName = RequiredName(request.FirstName, 100);
        person.LastName = Optional(request.LastName, 100);
        person.Email = Optional(request.Email, 320);
        person.Phone = Optional(request.Phone, 50);
        person.IsActive = request.IsActive;
    }

    private static PersonResponse Profile(Person p) => new(p.Id, p.Name, p.FirstName, p.LastName, p.Email, p.Phone, p.HouseholdId, p.IsActive);

    public async Task<PersonResponse> CreatePersonAsync(PersonRequest request, CancellationToken ct)
    {
        await scope.StaffAsync(ct);
        var person = new Person { FirstName = RequiredName(request.FirstName, 100) };
        SetPerson(person, request);
        db.People.Add(person);
        await scope.SaveAsync(ct);
        return Profile(person);
    }

    public async Task<PersonResponse> GetPersonAsync(Guid id, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        return Profile(await scope.People(actor).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Missing());
    }

    public async Task<PersonResponse> UpdatePersonAsync(Guid id, PersonRequest request, CancellationToken ct)
    {
        var actor = await scope.ActorAsync(ct);
        var person = await scope.People(actor).SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Missing();
        SetPerson(person, request);
        await scope.SaveAsync(ct);
        return Profile(person);
    }

    public async Task<PersonLookupResponse[]> LookupAsync(string? name, CancellationToken ct)
    {
        await scope.ActorAsync(ct);
        var term = Optional(name, 200);
        var now = scope.Now;
        return await db.People.Select(p => new
            {
                p.Id,
                p.IsActive,
                p.HouseholdId,
                Name = p.FirstName + (p.LastName == null ? string.Empty : " " + p.LastName)
            }).Where(p => p.IsActive && (term == null || p.Name.Contains(term)))
            .OrderBy(p => p.Name).ThenBy(p => p.Id).Take(50)
            .Select(p => new PersonLookupResponse(p.Id, p.Name,
                db.Contexts.Where(g => g.StartsAt <= now && (g.EndsAt == null || g.EndsAt > now)
                    && (db.ContextMembers.Any(m => m.PersonId == p.Id && m.ContextId == g.Id && m.JoinedAt <= now && (m.LeftAt == null || m.LeftAt > now))
                        || db.ContextLeaders.Any(l => l.PersonId == p.Id && l.ContextId == g.Id && l.StartsAt <= now && (l.EndsAt == null || l.EndsAt > now))))
                    .OrderBy(g => g.Name).Select(g => g.Name).ToArray(),
                db.Households.Where(h => h.Id == p.HouseholdId).Select(h => h.Label).FirstOrDefault(),
                db.People.Where(member => p.HouseholdId != null && member.HouseholdId == p.HouseholdId)
                    .OrderBy(member => member.Id).Select(member => member.FirstName).ToArray()))
            .ToArrayAsync(ct);
    }
}
