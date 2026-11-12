using Church = Sheaf.Data.Church;

namespace Sheaf.Core.Tests;

/// <summary>Churches A and B, each with a leader, two people, and a spoke of one shared global hub.</summary>
public sealed class TwoChurchSeed
{
    public required Church ChurchA { get; init; }
    public required Church ChurchB { get; init; }
    public required Leader LeaderA { get; init; }
    public required Leader LeaderB { get; init; }
    public required IReadOnlyList<Person> PeopleA { get; init; }
    public required IReadOnlyList<Person> PeopleB { get; init; }
    public required Hub Hub { get; init; }
    public required Spoke SpokeA { get; init; }
    public required Spoke SpokeB { get; init; }

    public static async Task<TwoChurchSeed> SeedAsync(Func<Guid?, TenancyTestDbContext> createContext)
    {
        Church churchA = new() { Slug = "church-a", Name = "Church A" };
        Church churchB = new() { Slug = "church-b", Name = "Church B" };
        Hub hub = new() { Id = Guid.NewGuid(), Name = "Shared Hub" };

        await using (TenancyTestDbContext shared = createContext(null))
        {
            shared.Churches.AddRange(churchA, churchB);
            shared.Hubs.Add(hub);
            await shared.SaveChangesAsync();
        }

        (Leader leaderA, List<Person> peopleA, Spoke spokeA) = await SeedChurchAsync(createContext, churchA, hub, "a");
        (Leader leaderB, List<Person> peopleB, Spoke spokeB) = await SeedChurchAsync(createContext, churchB, hub, "b");

        return new TwoChurchSeed
        {
            ChurchA = churchA,
            ChurchB = churchB,
            LeaderA = leaderA,
            LeaderB = leaderB,
            PeopleA = peopleA,
            PeopleB = peopleB,
            Hub = hub,
            SpokeA = spokeA,
            SpokeB = spokeB,
        };
    }

    private static async Task<(Leader, List<Person>, Spoke)> SeedChurchAsync(
        Func<Guid?, TenancyTestDbContext> createContext, Church church, Hub hub, string tag)
    {
        Leader leader = new() { Id = Guid.NewGuid(), ChurchId = church.Id, Name = $"Leader {tag}" };
        List<Person> people =
        [
            NewPerson(church, leader, tag, 1),
            NewPerson(church, leader, tag, 2),
        ];
        Spoke spoke = new() { Id = Guid.NewGuid(), ChurchId = church.Id, HubId = hub.Id, Name = $"Spoke {tag}" };

        await using TenancyTestDbContext db = createContext(church.Id);
        db.Leaders.Add(leader);
        db.People.AddRange(people);
        db.Spokes.Add(spoke);
        await db.SaveChangesAsync();
        return (leader, people, spoke);
    }

    private static Person NewPerson(Church church, Leader leader, string tag, int number) => new()
    {
        Id = Guid.NewGuid(),
        ChurchId = church.Id,
        Name = $"Person {tag}{number}",
        Email = $"person-{tag}{number}@example.com",
        LeaderId = leader.Id,
    };
}
