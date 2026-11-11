using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

using Sheaf.Data.Tenancy;

using Church = Sheaf.Data.Church;

namespace Sheaf.Core.Tests;

public class TenancyExecuteUpdateTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_RuntimeEfPropertyChurchIdSetter_ThrowsBeforeMutation(bool useAsync)
    {
        using SqliteFixture sqlite = new();
        BulkSeed seed = await SeedAsync(sqlite);
        await using (TenancyTestDbContext db = sqlite.CreateContext(seed.ChurchA.Id))
        {
            string propertyName = nameof(Leader.Id);
            Action<UpdateSettersBuilder<Leader>> setters = setters => setters.SetProperty(
                row => EF.Property<Guid>(row, propertyName),
                row => EF.Property<Guid>(row, propertyName));

            await Assert.That(await UpdateAsync(db.Leaders, setters, useAsync)).IsEqualTo(1);
            propertyName = nameof(Leader.ChurchId);

            await AssertImmutableAsync(() => UpdateAsync(db.Leaders, setters, useAsync));
        }

        await AssertOriginalLeadersAsync(sqlite, seed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_DynamicOwnershipSetterWithPayloadChange_RejectsEntireCommand(bool useAsync)
    {
        using SqliteFixture sqlite = new();
        BulkSeed seed = await SeedAsync(sqlite);
        ParameterExpression row = Expression.Parameter(typeof(Leader), "row");
        Expression<Func<Leader, Guid>> property = Expression.Lambda<Func<Leader, Guid>>(
            Expression.Property(row, nameof(Leader.ChurchId)), row);
        await using (TenancyTestDbContext db = sqlite.CreateContext(seed.ChurchA.Id))
        {
            await AssertImmutableAsync(() => UpdateAsync(db.Leaders, setters =>
            {
                setters.SetProperty(leader => leader.Name, "must-not-be-written");
                setters.SetProperty(property, seed.ChurchB.Id);
            }, useAsync));
        }

        await AssertOriginalLeadersAsync(sqlite, seed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_ProjectedEntityOwnershipSetter_ThrowsBeforeMutation(bool useAsync)
    {
        using SqliteFixture sqlite = new();
        BulkSeed seed = await SeedAsync(sqlite);
        await using (TenancyTestDbContext db = sqlite.CreateContext(seed.ChurchA.Id))
        {
            var query = db.Leaders.Select(leader => new { Nested = new { Entity = leader } });

            await AssertImmutableAsync(() => UpdateAsync(query,
                setters => setters.SetProperty(row => row.Nested.Entity.ChurchId, seed.ChurchB.Id),
                useAsync));
        }

        await AssertOriginalLeadersAsync(sqlite, seed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_ProjectedScalarOwnershipAlias_ThrowsBeforeMutation(bool useAsync)
    {
        using SqliteFixture sqlite = new();
        BulkSeed seed = await SeedAsync(sqlite);
        await using (TenancyTestDbContext db = sqlite.CreateContext(seed.ChurchA.Id))
        {
            var query = db.Leaders.Select(leader => new { Owner = leader.ChurchId })
                .Where(row => row.Owner == seed.ChurchA.Id)
                .Select(row => new { RenamedOwner = row.Owner });

            await AssertImmutableAsync(() => UpdateAsync(query,
                setters => setters.SetProperty(row => row.RenamedOwner, seed.ChurchB.Id), useAsync));
        }

        await AssertOriginalLeadersAsync(sqlite, seed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_CurrentChurchAssignmentWithOrWithoutMatches_ThrowsBeforeMutation(bool noMatches)
    {
        using SqliteFixture sqlite = new();
        BulkSeed seed = await SeedAsync(sqlite);
        await using (TenancyTestDbContext db = sqlite.CreateContext(seed.ChurchA.Id))
        {
            IQueryable<Leader> query = noMatches
                ? db.Leaders.Where(row => row.Id == Guid.Empty)
                : db.Leaders;

            await AssertImmutableAsync(() => query.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ChurchId, row => row.ChurchId)));
        }

        await AssertOriginalLeadersAsync(sqlite, seed);
    }

    [Test]
    public async Task ExecuteUpdate_CovariantTenantQuery_RejectsMappedOwnershipAssignment()
    {
        using SqliteFixture sqlite = new();
        BulkSeed seed = await SeedAsync(sqlite);
        await using (TenancyTestDbContext db = sqlite.CreateContext(seed.ChurchA.Id))
        {
            IQueryable<ITenanted> query = db.Leaders;
            await AssertImmutableAsync(() => query.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ChurchId, seed.ChurchB.Id)));
        }

        await AssertOriginalLeadersAsync(sqlite, seed);
    }

    [Test]
    public async Task ExecuteUpdate_ConcatenatedOwnershipAlias_ThrowsBeforeMutation()
    {
        using SqliteFixture sqlite = new();
        BulkSeed seed = await SeedAsync(sqlite);
        await using (TenancyTestDbContext db = sqlite.CreateContext(seed.ChurchA.Id))
        {
            var projection = db.Leaders.Select(row => new { Owner = row.ChurchId });
            await AssertImmutableAsync(() => projection.Concat(projection).ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.Owner, seed.ChurchB.Id)));
        }

        await AssertOriginalLeadersAsync(sqlite, seed);
    }

    [Test]
    public async Task ExecuteUpdate_PayloadAliasedChurchId_UpdatesEachTenantWithoutChangingOwnership()
    {
        using SqliteFixture sqlite = new();
        BulkSeed seed = await SeedAsync(sqlite);
        foreach (Church church in new[] { seed.ChurchA, seed.ChurchB })
        {
            await using TenancyTestDbContext db = sqlite.CreateContext(church.Id);
            string name = $"ChurchId is ordinary text for {church.Slug}";
            var query = db.Leaders.Select(leader => new { ChurchId = leader.Name })
                .Select(row => new { row.ChurchId });

            int affected = await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ChurchId, name));

            await Assert.That(affected).IsEqualTo(1);
        }

        foreach ((Church church, Leader leader) in seed.Churches)
        {
            await using TenancyTestDbContext db = sqlite.CreateContext(church.Id);
            Leader persisted = await db.Leaders.AsNoTracking().SingleAsync();
            await Assert.That(persisted.Id).IsEqualTo(leader.Id);
            await Assert.That(persisted.ChurchId).IsEqualTo(church.Id);
            await Assert.That(persisted.Name).IsEqualTo($"ChurchId is ordinary text for {church.Slug}");
        }
    }

    [Test]
    public async Task ExecuteUpdate_ProjectedEntityPayloadWithEfProperty_OnlyUpdatesCurrentTenant()
    {
        using SqliteFixture sqlite = new();
        BulkSeed seed = await SeedAsync(sqlite);
        const string name = """{"ChurchId":"ordinary payload"}""";
        await using (TenancyTestDbContext db = sqlite.CreateContext(seed.ChurchA.Id))
        {
            var query = db.Leaders.Select(leader => new { Entity = leader });
            int affected = query.ExecuteUpdate(setters => setters.SetProperty(
                row => EF.Property<string>(row.Entity, nameof(Leader.Name)), name));
            await Assert.That(affected).IsEqualTo(1);
        }

        foreach ((Church church, Leader leader) in seed.Churches)
        {
            await using TenancyTestDbContext db = sqlite.CreateContext(church.Id);
            Leader persisted = await db.Leaders.AsNoTracking().SingleAsync();
            await Assert.That(persisted.Id).IsEqualTo(leader.Id);
            await Assert.That(persisted.ChurchId).IsEqualTo(church.Id);
            await Assert.That(persisted.Name).IsEqualTo(church == seed.ChurchA ? name : leader.Name);
        }
    }

    [Test]
    public async Task ExecuteUpdate_GlobalPayloadAliasedChurchId_UpdatesMappedGlobalField()
    {
        using SqliteFixture sqlite = new();
        BulkSeed seed = await SeedAsync(sqlite);
        await using TenancyTestDbContext db = sqlite.CreateContext(null);

        int affected = await db.Churches.Where(church => church.Id == seed.ChurchA.Id)
            .Select(church => new { ChurchId = church.Name })
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ChurchId, "renamed global church"));

        await Assert.That(affected).IsEqualTo(1);
        await Assert.That(await db.Churches.Where(church => church.Id == seed.ChurchA.Id)
            .Select(church => church.Name).SingleAsync()).IsEqualTo("renamed global church");
        await AssertOriginalLeadersAsync(sqlite, seed);
    }

    private static Task<int> UpdateAsync<TEntity>(
        IQueryable<TEntity> query, Action<UpdateSettersBuilder<TEntity>> setters, bool useAsync) =>
        useAsync ? query.ExecuteUpdateAsync(setters) : Task.FromResult(query.ExecuteUpdate(setters));

    private static async Task AssertImmutableAsync(Func<Task<int>> update) =>
        await Assert.That(async () => await update())
            .Throws<InvalidOperationException>()
            .WithMessage("ChurchId is immutable and cannot be assigned by ExecuteUpdate.");

    private static async Task<BulkSeed> SeedAsync(SqliteFixture sqlite)
    {
        Church churchA = new() { Slug = "bulk-a", Name = "Bulk Church A" };
        Church churchB = new() { Slug = "bulk-b", Name = "Bulk Church B" };
        Leader leaderA = new() { Id = Guid.NewGuid(), ChurchId = churchA.Id, Name = "Bulk Leader A" };
        Leader leaderB = new() { Id = Guid.NewGuid(), ChurchId = churchB.Id, Name = "Bulk Leader B" };
        BulkSeed seed = new(churchA, churchB, leaderA, leaderB);
        await using (TenancyTestDbContext db = sqlite.CreateContext(null))
        {
            db.Churches.AddRange(churchA, churchB);
            await db.SaveChangesAsync();
        }

        foreach ((Church church, Leader leader) in seed.Churches)
        {
            await using TenancyTestDbContext db = sqlite.CreateContext(church.Id);
            db.Leaders.Add(leader);
            await db.SaveChangesAsync();
        }

        return seed;
    }

    private static async Task AssertOriginalLeadersAsync(SqliteFixture sqlite, BulkSeed seed)
    {
        foreach ((Church church, Leader leader) in seed.Churches)
        {
            await using TenancyTestDbContext db = sqlite.CreateContext(church.Id);
            Leader persisted = await db.Leaders.AsNoTracking().SingleAsync();
            await Assert.That(persisted.Id).IsEqualTo(leader.Id);
            await Assert.That(persisted.ChurchId).IsEqualTo(church.Id);
            await Assert.That(persisted.Name).IsEqualTo(leader.Name);
        }
    }

    private sealed record BulkSeed(Church ChurchA, Church ChurchB, Leader LeaderA, Leader LeaderB)
    {
        public (Church Church, Leader Leader)[] Churches => [(ChurchA, LeaderA), (ChurchB, LeaderB)];
    }
}
