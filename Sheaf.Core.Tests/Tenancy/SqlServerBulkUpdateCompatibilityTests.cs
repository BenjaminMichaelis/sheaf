using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

[NotInParallel("sql-server")]
public class SqlServerBulkUpdateCompatibilityTests
{
    [ClassDataSource<TenancyBulkSqlServerFixture>(Shared = SharedType.PerClass)]
    public required TenancyBulkSqlServerFixture Sql { get; init; }

    [Before(Test)]
    public Task ResetAsync() => Sql.ResetAsync();

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_CurrentChurchPayload_OnlyUpdatesCurrentChurchRecords(bool useAsync)
    {
        TwoChurchTenancyScenario seed = await TenancyBulkTestData.SeedTwoChurchesAsync(Sql);
        int affected = await Sql.InDatabaseScopeAsync(seed.ChurchAId, db => UpdateAsync(db.Records,
            setters => setters.SetProperty(record => record.Value, "updated"), useAsync));

        await Assert.That(affected).IsEqualTo(1);
        await seed.AssertRecordsAsync(Sql, valueA: "updated");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteDelete_CurrentChurchQuery_OnlyDeletesCurrentChurchRecords(bool useAsync)
    {
        TwoChurchTenancyScenario seed = await TenancyBulkTestData.SeedTwoChurchesAsync(Sql);
        int affected = await Sql.InDatabaseScopeAsync(seed.ChurchAId, db => DeleteAsync(db.Records, useAsync));

        await Assert.That(affected).IsEqualTo(1);
        Guid[] remaining = await Sql.InDatabaseScopeAsync(seed.ChurchAId, db => db.Records.IgnoreQueryFilters()
            .Select(record => record.Id).ToArrayAsync());
        await Assert.That(remaining).IsEquivalentTo([seed.RecordB.Id]);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExecuteUpdate_ChurchIdAssignmentWithOrWithoutMatches_RejectsEntireCommand(
        bool useAsync, bool noMatches)
    {
        TwoChurchTenancyScenario seed = await TenancyBulkTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchAId, async db =>
        {
            IQueryable<TenancyBulkTestRecord> query = noMatches
                ? db.Records.Where(record => record.Id == Guid.Empty)
                : db.Records;

            await AssertImmutableAsync(() => UpdateAsync(query, setters =>
            {
                setters.SetProperty(record => record.Value, "changed");
                setters.SetProperty(record => record.ChurchId, seed.ChurchBId);
            }, useAsync));
            await AssertImmutableAsync(() => UpdateAsync(query,
                setters => setters.SetProperty(record => record.ChurchId, record => record.ChurchId), useAsync));
        });

        await seed.AssertRecordsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_RuntimeEfPropertyOwnershipSetter_RejectsBeforeMutation(bool useAsync)
    {
        TwoChurchTenancyScenario seed = await TenancyBulkTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchAId, async db =>
        {
            string propertyName = nameof(TenancyBulkTestRecord.Id);
            Action<UpdateSettersBuilder<TenancyBulkTestRecord>> setters = setters => setters.SetProperty(
                record => EF.Property<Guid>(record, propertyName),
                record => EF.Property<Guid>(record, propertyName));

            await Assert.That(await UpdateAsync(db.Records, setters, useAsync)).IsEqualTo(1);
            propertyName = nameof(TenancyBulkTestRecord.ChurchId);
            await AssertImmutableAsync(() => UpdateAsync(db.Records, setters, useAsync));
        });

        await seed.AssertRecordsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_UnionOrCorrelatedRecordSource_UpdatesPayloadButRejectsOwnership(bool useUnion)
    {
        TwoChurchTenancyScenario seed = await TenancyBulkTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchAId, async db =>
        {
            IQueryable<TenancyBulkTestRecord> query = useUnion
                ? db.Records.Where(record => record.Value != string.Empty)
                    .Union(db.Records.Where(record => record.Value != string.Empty))
                : db.Records.SelectMany(
                    outer => db.Records.Where(inner => inner.Id == outer.Id),
                    (outer, _) => outer);

            int affected = await query.ExecuteUpdateAsync(
                setters => setters.SetProperty(record => record.Value, "updated"));

            await Assert.That(affected).IsEqualTo(1);
            await AssertImmutableAsync(() => query.ExecuteUpdateAsync(
                setters => setters.SetProperty(record => record.ChurchId, seed.ChurchBId)));
        });

        await seed.AssertRecordsAsync(Sql, valueA: "updated");
    }

    [Test]
    public async Task ExecuteUpdate_PayloadAliasedChurchId_UpdatesValueWithoutChangingOwnership()
    {
        TwoChurchTenancyScenario seed = await TenancyBulkTestData.SeedTwoChurchesAsync(Sql);
        int affected = await Sql.InDatabaseScopeAsync(seed.ChurchAId, db => db.Records
            .Select(record => new { ChurchId = record.Value })
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ChurchId, "updated")));

        await Assert.That(affected).IsEqualTo(1);
        await seed.AssertRecordsAsync(Sql, valueA: "updated");
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExecuteUpdateDelete_IgnoredTenancyFilter_RejectsBeforeMutation(bool useAsync, bool namedFilter)
    {
        TwoChurchTenancyScenario seed = await TenancyBulkTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchAId, async db =>
        {
            IQueryable<TenancyBulkTestRecord> query = namedFilter
                ? db.Records.IgnoreQueryFilters([MultiTenancyConvention.FilterName])
                : db.Records.IgnoreQueryFilters();

            await Assert.That(async () => await UpdateAsync(query,
                    setters => setters.SetProperty(record => record.Value, "updated"), useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("ExecuteUpdate cannot ignore the TenancyFilter");
            await Assert.That(async () => await DeleteAsync(query, useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("ExecuteDelete cannot ignore the TenancyFilter");
        });

        await seed.AssertRecordsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteUpdate_GlobalRecordWriteWithTenantedQueryRoot_RejectsBeforeMutation(bool useAsync)
    {
        TwoChurchTenancyScenario seed = await TenancyBulkTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchAId, async db =>
        {
            await Assert.That(async () => await UpdateGlobalRecordsAsync(GetGlobalRecordsWithTenantedRoot(db), useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("ExecuteUpdate cannot ignore the TenancyFilter");
        });

        await AssertGlobalsAndRecordsUnchangedAsync(seed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteDelete_GlobalRecordWriteWithTenantedQueryRoot_RejectsBeforeMutation(bool useAsync)
    {
        TwoChurchTenancyScenario seed = await TenancyBulkTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchAId, async db =>
        {
            await Assert.That(async () => await DeleteGlobalRecordsAsync(GetGlobalRecordsWithTenantedRoot(db), useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("ExecuteDelete cannot ignore the TenancyFilter");
        });

        await AssertGlobalsAndRecordsUnchangedAsync(seed);
    }

    private async Task AssertGlobalsAndRecordsUnchangedAsync(TwoChurchTenancyScenario seed)
    {
        TenancyBulkGlobalRecord[] persisted = await Sql.InDatabaseScopeAsync(seed.ChurchAId,
            db => db.GlobalRecords.AsNoTracking().ToArrayAsync());
        await Assert.That(persisted.Select(record => (record.Id, record.Value))).IsEquivalentTo([
            (seed.GlobalA.Id, seed.GlobalA.Value),
            (seed.GlobalB.Id, seed.GlobalB.Value)
        ]);
        await seed.AssertRecordsAsync(Sql);
    }

    private static IQueryable<TenancyBulkGlobalRecord> GetGlobalRecordsWithTenantedRoot(
        TenancyBulkTestContext db) =>
        db.GlobalRecords.Where(global => db.Records.Any(record => record.Value == global.Value))
            .IgnoreQueryFilters();

    [Test]
    public async Task ExecuteUpdate_IgnoreUnrelatedNamedFilter_OnlyUpdatesCurrentChurch()
    {
        TwoChurchTenancyScenario seed = await TenancyBulkTestData.SeedTwoChurchesAsync(Sql);
        int affected = await Sql.InDatabaseScopeAsync(seed.ChurchAId, db => db.Records
            .IgnoreQueryFilters(["Unrelated"])
            .ExecuteUpdateAsync(setters => setters.SetProperty(record => record.Value, "updated")));

        await Assert.That(affected).IsEqualTo(1);
        await seed.AssertRecordsAsync(Sql, valueA: "updated");
    }

    private static Task<int> UpdateAsync<TEntity>(
        IQueryable<TEntity> query, Action<UpdateSettersBuilder<TEntity>> setters, bool useAsync) =>
        useAsync ? query.ExecuteUpdateAsync(setters) : Task.FromResult(query.ExecuteUpdate(setters));

    private static Task<int> DeleteAsync<TEntity>(IQueryable<TEntity> query, bool useAsync) =>
        useAsync ? query.ExecuteDeleteAsync() : Task.FromResult(query.ExecuteDelete());

    private static Task<int> UpdateGlobalRecordsAsync(
        IQueryable<TenancyBulkGlobalRecord> records, bool useAsync) =>
        useAsync
            ? records.ExecuteUpdateAsync(setters => setters.SetProperty(record => record.Value, "changed"))
            : Task.FromResult(records.ExecuteUpdate(setters => setters.SetProperty(record => record.Value, "changed")));

    private static Task<int> DeleteGlobalRecordsAsync(IQueryable<TenancyBulkGlobalRecord> records, bool useAsync) =>
        useAsync ? records.ExecuteDeleteAsync() : Task.FromResult(records.ExecuteDelete());

    private static async Task AssertImmutableAsync(Func<Task<int>> update) =>
        await Assert.That(async () => await update())
            .Throws<InvalidOperationException>()
            .WithMessage("ChurchId is immutable and cannot be assigned by ExecuteUpdate.");
}
