using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Sheaf.Data.Tenancy;

using TUnit.Core.Interfaces;

namespace Sheaf.Core.Tests;

internal sealed class TenancyBulkTestContext(DbContextOptions<TenancyBulkTestContext> options, Guid churchId)
    : DbContext(options), ITenantContext
{
    public Guid ChurchId { get; } = churchId;

    public DbSet<TenancyBulkTestRecord> Records => Set<TenancyBulkTestRecord>();

    public DbSet<TenancyBulkGlobalRecord> GlobalRecords => Set<TenancyBulkGlobalRecord>();
}

internal sealed class TenancyBulkTestRecord : ITenanted
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChurchId { get; set; }

    public required string Value { get; set; }
}

internal sealed class TenancyBulkGlobalRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Value { get; set; }
}

public sealed class TenancyBulkSqlServerFixture : IAsyncInitializer
{
    private string? _connectionString;

    [ClassDataSource<SqlServerFixture>(Shared = SharedType.PerAssembly)]
    public required SqlServerFixture Sql { get; init; }

    public async Task InitializeAsync()
    {
        _connectionString = new SqlConnectionStringBuilder(Sql.ConnectionString)
        {
            InitialCatalog = $"SheafTenancyBulk_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using TenancyBulkTestContext db = CreateContext(Guid.NewGuid());
        await db.Database.EnsureCreatedAsync();
    }

    internal TenancyBulkTestContext CreateContext(Guid churchId)
    {
        string connectionString = _connectionString
            ?? throw new InvalidOperationException("The fixture is not initialized.");
        DbContextOptions<TenancyBulkTestContext> options = new DbContextOptionsBuilder<TenancyBulkTestContext>()
            .UseSqlServer(connectionString)
            .UseMultiTenancy()
            .Options;
        return new TenancyBulkTestContext(options, churchId);
    }

    internal async Task<T> InDatabaseScopeAsync<T>(Guid churchId, Func<TenancyBulkTestContext, Task<T>> action)
    {
        await using TenancyBulkTestContext db = CreateContext(churchId);
        return await action(db);
    }

    internal async Task InDatabaseScopeAsync(Guid churchId, Func<TenancyBulkTestContext, Task> action)
    {
        await using TenancyBulkTestContext db = CreateContext(churchId);
        await action(db);
    }

    internal async Task ResetAsync()
    {
        Guid churchId = Guid.NewGuid();
        await using TenancyBulkTestContext db = CreateContext(churchId);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }
}

internal sealed record TwoChurchTenancyScenario(
    Guid ChurchAId,
    Guid ChurchBId,
    TenancyBulkGlobalRecord GlobalA,
    TenancyBulkGlobalRecord GlobalB,
    TenancyBulkTestRecord RecordA,
    TenancyBulkTestRecord RecordB)
{
    public async Task AssertRecordsAsync(
        TenancyBulkSqlServerFixture sql,
        string valueA = "A",
        string valueB = "B")
    {
        TenancyBulkTestRecord[] persisted = await sql.InDatabaseScopeAsync(ChurchAId,
            db => db.Records.IgnoreQueryFilters().AsNoTracking().ToArrayAsync());

        await Assert.That(persisted.Select(record => (record.Id, record.ChurchId, record.Value)))
            .IsEquivalentTo([
                (RecordA.Id, ChurchAId, valueA),
                (RecordB.Id, ChurchBId, valueB)
            ]);
    }
}

internal static class TenancyBulkTestData
{
    public static async Task<TwoChurchTenancyScenario> SeedTwoChurchesAsync(TenancyBulkSqlServerFixture sql)
    {
        Guid churchAId = Guid.NewGuid();
        Guid churchBId = Guid.NewGuid();
        TenancyBulkGlobalRecord globalA = new() { Value = "A" };
        TenancyBulkGlobalRecord globalB = new() { Value = "B" };
        TenancyBulkTestRecord recordA = new() { ChurchId = churchAId, Value = "A" };
        TenancyBulkTestRecord recordB = new() { ChurchId = churchBId, Value = "B" };

        await sql.InDatabaseScopeAsync(churchAId, async db =>
        {
            db.GlobalRecords.AddRange(globalA, globalB);
            await db.SaveChangesAsync();
            db.Records.Add(recordA);
            await db.SaveChangesAsync();
        });
        await sql.InDatabaseScopeAsync(churchBId, async db =>
        {
            db.Records.Add(recordB);
            await db.SaveChangesAsync();
        });

        return new(churchAId, churchBId, globalA, globalB, recordA, recordB);
    }
}
