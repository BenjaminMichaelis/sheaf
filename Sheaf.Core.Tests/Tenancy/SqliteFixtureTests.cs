using Microsoft.EntityFrameworkCore;

namespace Sheaf.Core.Tests;

public class SqliteFixtureTests
{
    [Test]
    public async Task CreateContext_SeparateContexts_ShareTheSeededDatabase()
    {
        using SqliteFixture sqlite = new();
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(sqlite.CreateContext);

        await using TenancyTestDbContext asA = sqlite.CreateContext(seed.ChurchA.Id);
        List<Person> people = await asA.People.ToListAsync();

        await Assert.That(people.Select(p => p.Id)).IsEquivalentTo(seed.PeopleA.Select(p => p.Id));
    }
}
