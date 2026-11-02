using Church = Sheaf.Data.Church;

namespace Sheaf.Core.Tests;

/// <summary>Churches A and B, each with a leader and two people.</summary>
public sealed class TwoChurchSeed
{
    public required Church ChurchA { get; init; }
    public required Church ChurchB { get; init; }
    public required Leader LeaderA { get; init; }
    public required Leader LeaderB { get; init; }
    public required IReadOnlyList<Person> PeopleA { get; init; }
    public required IReadOnlyList<Person> PeopleB { get; init; }

    public static async Task<TwoChurchSeed> SeedAsync(Func<Guid?, TenancyTestDbContext> createContext)
    {
        Church churchA = new() { Slug = "church-a", Name = "Church A" };
        Church churchB = new() { Slug = "church-b", Name = "Church B" };

        await using (TenancyTestDbContext shared = createContext(null))
        {
            shared.Churches.AddRange(churchA, churchB);
            await shared.SaveChangesAsync();
        }

        (Leader leaderA, List<Person> peopleA) = await SeedChurchAsync(createContext, churchA, "a");
        (Leader leaderB, List<Person> peopleB) = await SeedChurchAsync(createContext, churchB, "b");

        return new TwoChurchSeed
        {
            ChurchA = churchA,
            ChurchB = churchB,
            LeaderA = leaderA,
            LeaderB = leaderB,
            PeopleA = peopleA,
            PeopleB = peopleB,
        };
    }

    private static async Task<(Leader, List<Person>)> SeedChurchAsync(
        Func<Guid?, TenancyTestDbContext> createContext, Church church, string tag)
    {
        Leader leader = new() { Id = Guid.NewGuid(), ChurchId = church.Id, Name = $"Leader {tag}" };
        List<Person> people =
        [
            NewPerson(church, leader, tag, 1),
            NewPerson(church, leader, tag, 2),
        ];

        await using TenancyTestDbContext db = createContext(church.Id);
        db.Leaders.Add(leader);
        db.People.AddRange(people);
        await db.SaveChangesAsync();
        return (leader, people);
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
