using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

using Sheaf.Data;
using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

[NotInParallel("sql-server")]
public class SqlServerBulkUpdateCompatibilityTests
{
    [ClassDataSource<ProductionSqlServerFixture>(Shared = SharedType.PerClass)]
    public required ProductionSqlServerFixture Sql { get; init; }

    [Before(Test)]
    public async Task MigrateAndResetAsync()
    {
        await Sql.InDatabaseScopeAsync(null, db => db.Database.MigrateAsync());
        await Sql.ResetAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_CurrentChurchPayload_OnlyUpdatesCurrentChurchMemberships(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        int affected = await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, db => UpdateAsync(db.ChurchMemberships,
            setters => setters.SetProperty(membership => membership.Role, ChurchMembershipRole.Admin), useAsync));

        await Assert.That(affected).IsEqualTo(1);
        await seed.AssertMembershipsAsync(Sql, roleA: ChurchMembershipRole.Admin);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteDelete_CurrentChurchQuery_OnlyDeletesCurrentChurchMemberships(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        int affected = await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, db => DeleteAsync(db.ChurchMemberships, useAsync));

        await Assert.That(affected).IsEqualTo(1);
        Guid[] remaining = await Sql.InDatabaseScopeAsync(null, db => db.ChurchMemberships.IgnoreQueryFilters()
            .Select(membership => membership.Id).ToArrayAsync());
        await Assert.That(remaining).IsEquivalentTo([seed.MembershipB.Id]);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExecuteUpdate_ChurchIdAssignmentWithOrWithoutMatches_RejectsEntireCommand(bool useAsync, bool noMatches)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            IQueryable<ChurchMembership> query = noMatches
                ? db.ChurchMemberships.Where(membership => membership.Id == Guid.Empty)
                : db.ChurchMemberships;

            await AssertImmutableAsync(() => UpdateAsync(query, setters =>
            {
                setters.SetProperty(membership => membership.Role, ChurchMembershipRole.Admin);
                setters.SetProperty(membership => membership.ChurchId, seed.ChurchB.Id);
            }, useAsync));
            await AssertImmutableAsync(() => UpdateAsync(query,
                setters => setters.SetProperty(membership => membership.ChurchId, membership => membership.ChurchId), useAsync));
        });

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_RuntimeEfPropertyOwnershipSetter_RejectsBeforeMutation(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            string propertyName = nameof(ChurchMembership.Id);
            Action<UpdateSettersBuilder<ChurchMembership>> setters = setters => setters.SetProperty(
                membership => EF.Property<Guid>(membership, propertyName),
                membership => EF.Property<Guid>(membership, propertyName));

            await Assert.That(await UpdateAsync(db.ChurchMemberships, setters, useAsync)).IsEqualTo(1);
            propertyName = nameof(ChurchMembership.ChurchId);
            await AssertImmutableAsync(() => UpdateAsync(db.ChurchMemberships, setters, useAsync));
        });

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_UnionOrCorrelatedMembershipSource_UpdatesPayloadButRejectsOwnership(bool useUnion)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            IQueryable<ChurchMembership> query = useUnion
                ? db.ChurchMemberships.Where(membership => membership.UserId != string.Empty)
                    .Union(db.ChurchMemberships.Where(membership => membership.UserId != string.Empty))
                : db.ChurchMemberships.SelectMany(
                    outer => db.ChurchMemberships.Where(inner => inner.Id == outer.Id),
                    (outer, _) => outer);

            int affected = await query.ExecuteUpdateAsync(
                setters => setters.SetProperty(membership => membership.Role, ChurchMembershipRole.Admin));

            await Assert.That(affected).IsEqualTo(1);
            await AssertImmutableAsync(() => query.ExecuteUpdateAsync(
                setters => setters.SetProperty(membership => membership.ChurchId, seed.ChurchB.Id)));
        });

        await seed.AssertMembershipsAsync(Sql, roleA: ChurchMembershipRole.Admin);
    }

    [Test]
    public async Task ExecuteUpdate_PayloadAliasedChurchId_UpdatesRoleWithoutChangingOwnership()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        int affected = await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, db => db.ChurchMemberships
            .Select(membership => new { ChurchId = membership.Role })
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ChurchId, ChurchMembershipRole.Coordinator)));

        await Assert.That(affected).IsEqualTo(1);
        await seed.AssertMembershipsAsync(Sql, roleA: ChurchMembershipRole.Coordinator);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExecuteUpdateDelete_IgnoredTenancyFilter_RejectsBeforeMutation(bool useAsync, bool namedFilter)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            IQueryable<ChurchMembership> query = namedFilter
                ? db.ChurchMemberships.IgnoreQueryFilters([MultiTenancyConvention.FilterName])
                : db.ChurchMemberships.IgnoreQueryFilters();

            await Assert.That(async () => await UpdateAsync(query,
                    setters => setters.SetProperty(membership => membership.Role, ChurchMembershipRole.Admin), useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("ExecuteUpdate cannot ignore the TenancyFilter");
            await Assert.That(async () => await DeleteAsync(query, useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("ExecuteDelete cannot ignore the TenancyFilter");
        });

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    public async Task ExecuteUpdate_IgnoreUnrelatedNamedFilter_OnlyUpdatesCurrentChurch()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        int affected = await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, db => db.ChurchMemberships
            .IgnoreQueryFilters(["Unrelated"])
            .ExecuteUpdateAsync(setters => setters.SetProperty(membership => membership.Role, ChurchMembershipRole.Staff)));

        await Assert.That(affected).IsEqualTo(1);
        await seed.AssertMembershipsAsync(Sql, roleA: ChurchMembershipRole.Staff);
    }

    private static Task<int> UpdateAsync<TEntity>(
        IQueryable<TEntity> query, Action<UpdateSettersBuilder<TEntity>> setters, bool useAsync) =>
        useAsync ? query.ExecuteUpdateAsync(setters) : Task.FromResult(query.ExecuteUpdate(setters));

    private static Task<int> DeleteAsync<TEntity>(IQueryable<TEntity> query, bool useAsync) =>
        useAsync ? query.ExecuteDeleteAsync() : Task.FromResult(query.ExecuteDelete());

    private static async Task AssertImmutableAsync(Func<Task<int>> update) =>
        await Assert.That(async () => await update())
            .Throws<InvalidOperationException>()
            .WithMessage("ChurchId is immutable and cannot be assigned by ExecuteUpdate.");
}
