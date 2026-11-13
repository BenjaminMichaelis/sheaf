using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheaf.Data;

using TUnit.Assertions.Enums;

namespace Sheaf.Core.Tests;

public class ProductionMigrationModelTests
{
    [Test]
    public async Task HasPendingModelChanges_ProductionModelFromAddDatabase_ReturnsFalse()
    {
        using IHost host = ProductionDatabaseHost.Create(
            "Server=127.0.0.1,1;Database=SheafProductionModelTests;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=1");
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.That(db.Database.HasPendingModelChanges()).IsFalse();
    }
}

public class SqlServerMigrationTests
{
    [Test]
    [ClassDataSource<ProductionSqlServerFixture>]
    public async Task MigrateAsync_FreshSqlServer_AppliesEveryMigrationInOrder(ProductionSqlServerFixture sql)
    {
        await sql.InDatabaseScopeAsync(null, async db =>
        {
            string[] available = db.Database.GetMigrations().ToArray();

            await Assert.That(available).IsNotEmpty();
            await Assert.That(await db.Database.GetAppliedMigrationsAsync()).IsEmpty();

            await db.Database.MigrateAsync();

            string[] applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            await Assert.That(applied).IsEquivalentTo(available, CollectionOrdering.Matching);
            await Assert.That(await db.Database.GetPendingMigrationsAsync()).IsEmpty();
            await Assert.That(await db.Churches.AsNoTracking().ToListAsync()).IsEmpty();
            await Assert.That(await db.Users.AsNoTracking().ToListAsync()).IsEmpty();
            await Assert.That(await db.ChurchMemberships.IgnoreQueryFilters().AsNoTracking().ToListAsync()).IsEmpty();
        });
    }

    [Test]
    [ClassDataSource<ProductionSqlServerFixture>]
    public async Task ResetAsync_MigratedSqlServer_PreservesMigrationHistoryAndRemovesSeedRows(ProductionSqlServerFixture sql)
    {
        string[] available = await sql.InDatabaseScopeAsync(null, async db =>
        {
            string[] migrations = db.Database.GetMigrations().ToArray();
            await Assert.That(migrations).IsNotEmpty();
            await db.Database.MigrateAsync();
            return migrations;
        });

        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(sql);
        await seed.AssertMembershipsAsync(sql);
        await sql.ResetAsync();

        await sql.InDatabaseScopeAsync(null, async verify =>
        {
            await Assert.That(await verify.Database.GetAppliedMigrationsAsync())
                .IsEquivalentTo(available, CollectionOrdering.Matching);
            await Assert.That(await verify.Database.GetPendingMigrationsAsync()).IsEmpty();
            await Assert.That(await verify.Churches.AsNoTracking().ToListAsync()).IsEmpty();
            await Assert.That(await verify.Users.AsNoTracking().ToListAsync()).IsEmpty();
            await Assert.That(await verify.ChurchMemberships.IgnoreQueryFilters().AsNoTracking().ToListAsync()).IsEmpty();
        });
    }
}
