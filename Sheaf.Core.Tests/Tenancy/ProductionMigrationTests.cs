using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    public async Task MigrateAsync_AccountConfirmedBeforeEmailVerification_MustVerifyAgainWithNewStamp(ProductionSqlServerFixture sql)
    {
        const string userId = "legacy-account";
        const string oldStamp = "legacy-stamp";
        await sql.InDatabaseScopeAsync(null, async db =>
        {
            // Registration used to confirm every email without sending anything.
            await db.GetService<IMigrator>().MigrateAsync("20261126022815_Attendance");
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO [AspNetUsers] ([Id], [UserName], [NormalizedUserName], [Email], [NormalizedEmail], [EmailConfirmed],
                    [SecurityStamp], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount])
                VALUES ({userId}, 'legacy@example.com', 'LEGACY@EXAMPLE.COM', 'legacy@example.com', 'LEGACY@EXAMPLE.COM', CAST(1 AS bit),
                    {oldStamp}, CAST(0 AS bit), CAST(0 AS bit), CAST(1 AS bit), 0)
                """);

            await db.Database.MigrateAsync();

            ApplicationUser user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            await Assert.That(user.EmailConfirmed).IsFalse();
            await Assert.That(user.SecurityStamp).IsNotEqualTo(oldStamp);
            await Assert.That((user.FirstName, user.LastName)).IsEqualTo(("", ""));
        });
    }

    [Test]
    [ClassDataSource<ProductionSqlServerFixture>]
    public async Task MigrateAsync_SecondAccountWithSameEmail_RejectedByDatabase(ProductionSqlServerFixture sql)
    {
        // Identity checks uniqueness before saving; the index is what stops two racing registrations.
        await sql.InDatabaseScopeAsync(null, async db =>
        {
            await db.Database.MigrateAsync();
            await InsertUserAsync(db, "first", "FIRST@EXAMPLE.COM", "SAME@EXAMPLE.COM");

            await Assert.That(async () => await InsertUserAsync(db, "second", "SECOND@EXAMPLE.COM", "SAME@EXAMPLE.COM"))
                .Throws<DbException>();
        });
    }

    private static Task<int> InsertUserAsync(ApplicationDbContext db, string id, string normalizedUserName, string normalizedEmail) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO [AspNetUsers] ([Id], [UserName], [NormalizedUserName], [Email], [NormalizedEmail], [EmailConfirmed],
                [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount], [FirstName], [LastName])
            VALUES ({id}, {normalizedUserName}, {normalizedUserName}, {normalizedEmail}, {normalizedEmail}, CAST(1 AS bit),
                CAST(0 AS bit), CAST(0 AS bit), CAST(1 AS bit), 0, '', '')
            """);

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
