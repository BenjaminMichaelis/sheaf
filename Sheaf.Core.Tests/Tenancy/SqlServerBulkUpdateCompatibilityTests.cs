using Microsoft.EntityFrameworkCore;

using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

[NotInParallel("sql-server")]
public class SqlServerBulkUpdateCompatibilityTests
{
    [ClassDataSource<SqlServerFixture>(Shared = SharedType.PerAssembly)]
    public required SqlServerFixture Sql { get; init; }

    [Before(Test)]
    public async Task ResetDatabaseAsync() => await Sql.ResetAsync();

    [Test]
    public async Task ExecuteUpdateAsync_CurrentTenantQuery_OnlyUpdatesCurrentChurchRows()
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);

        await using (TenancyTestDbContext db = Sql.CreateContext(seed.ChurchA.Id))
        {
            int affected = await db.Leaders.ExecuteUpdateAsync(
                setters => setters.SetProperty(leader => leader.Name, "updated"));
            await Assert.That(affected).IsEqualTo(1);
        }

        await AssertLeaderNamesAsync(seed, "updated", seed.LeaderB.Name);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdateAsync_UnionOrCorrelatedSourceChurchIdSetter_RejectsBeforeMutation(bool useUnion)
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);
        await using (TenancyTestDbContext db = Sql.CreateContext(seed.ChurchA.Id))
        {
            IQueryable<Leader> query = useUnion
                ? db.Leaders.Where(leader => leader.Name != string.Empty)
                    .Union(db.Leaders.Where(leader => leader.Name != string.Empty))
                : db.Leaders.SelectMany(
                    outer => db.Leaders.Where(inner => inner.Id == outer.Id),
                    (outer, _) => outer);

            await Assert.That(async () => await query.ExecuteUpdateAsync(
                    setters => setters.SetProperty(leader => leader.ChurchId, seed.ChurchB.Id)))
                .Throws<InvalidOperationException>()
                .WithMessage("ChurchId is immutable and cannot be assigned by ExecuteUpdate.");
        }

        await AssertLeaderNamesAsync(seed, seed.LeaderA.Name, seed.LeaderB.Name);
    }

    [Test]
    public async Task ExecuteUpdateAsync_UnionPayloadSetter_OnlyUpdatesCurrentChurchRows()
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);

        await using (TenancyTestDbContext db = Sql.CreateContext(seed.ChurchA.Id))
        {
            IQueryable<Leader> query = db.Leaders.Where(leader => leader.Name != string.Empty)
                .Union(db.Leaders.Where(leader => leader.Name != string.Empty));

            int affected = await query.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.Name, "payload-updated"));
            await Assert.That(affected).IsEqualTo(1);
        }

        await AssertLeaderNamesAsync(seed, "payload-updated", seed.LeaderB.Name);
    }

    [Test]
    public async Task ExecuteDeleteAsync_IgnoreQueryFiltersOnTenantedEntity_RejectsBeforeMutation()
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);
        await using (TenancyTestDbContext db = Sql.CreateContext(seed.ChurchA.Id))
        {
            await Assert.That(async () => await db.Leaders.IgnoreQueryFilters().ExecuteDeleteAsync())
                .Throws<InvalidOperationException>()
                .WithMessage($"ExecuteDelete cannot ignore the {MultiTenancyConvention.FilterName} query filter "
                    + "on tenanted entities; bulk writes must stay scoped to the current church.");
        }

        await AssertLeaderNamesAsync(seed, seed.LeaderA.Name, seed.LeaderB.Name);
    }

    private async Task AssertLeaderNamesAsync(TwoChurchSeed seed, string churchAName, string churchBName)
    {
        await using TenancyTestDbContext churchA = Sql.CreateContext(seed.ChurchA.Id);
        await using TenancyTestDbContext churchB = Sql.CreateContext(seed.ChurchB.Id);

        await Assert.That(await churchA.Leaders.Select(leader => leader.Name).SingleAsync()).IsEqualTo(churchAName);
        await Assert.That(await churchB.Leaders.Select(leader => leader.Name).SingleAsync()).IsEqualTo(churchBName);
        await Assert.That(await churchA.Leaders.IgnoreQueryFilters().CountAsync()).IsEqualTo(2);
    }
}
