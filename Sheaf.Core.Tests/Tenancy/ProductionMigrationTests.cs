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
    public async Task MigrateAsync_ExistingTenancyMembership_PreservesRowsAndAddsDomainSchema(
        ProductionSqlServerFixture sql)
    {
        const string foundation = "20261111195222_TenancyMembership";
        Church church = new() { Slug = "upgrade-church", Name = "Upgrade Church" };
        ApplicationUser user = new() { Id = "upgrade-user", UserName = "upgrade-user" };
        ChurchMembership membership = new()
        {
            ChurchId = church.Id,
            UserId = user.Id,
            Role = ChurchMembershipRole.Coordinator
        };

        await using (AsyncServiceScope scope = sql.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.GetService<IMigrator>().MigrateAsync(foundation);
            await Assert.That((await db.Database.GetAppliedMigrationsAsync()).Last()).IsEqualTo(foundation);
            db.Churches.Add(church);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = sql.CreateScope(church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChurchMemberships.Add(membership);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = sql.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await Assert.That(await db.Database.GetPendingMigrationsAsync())
                .IsEquivalentTo(["20261111202405_DomainModelV1"]);
            await db.Database.MigrateAsync();
            await Assert.That(await db.Database.GetPendingMigrationsAsync()).IsEmpty();
        }

        await using AsyncServiceScope verifyScope = sql.CreateScope(church.Id);
        ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ChurchMembership persisted = await verify.ChurchMemberships.AsNoTracking().SingleAsync();
        await Assert.That(persisted.Id).IsEqualTo(membership.Id);
        await Assert.That(persisted.ChurchId).IsEqualTo(church.Id);
        await Assert.That(persisted.UserId).IsEqualTo(user.Id);
        await Assert.That(persisted.Role).IsEqualTo(ChurchMembershipRole.Coordinator);
        Sheaf.Data.Context group = new() { Name = "Upgrade group", Kind = ContextKind.Group };
        verify.Contexts.Add(group);
        await verify.SaveChangesAsync();
        verify.ContextLeaders.Add(new ContextLeader
        {
            ContextId = group.Id,
            LeaderId = membership.Id
        });
        await verify.SaveChangesAsync();
        await Assert.That(await verify.ContextLeaders.AsNoTracking().Select(leader => leader.LeaderId).SingleAsync())
            .IsEqualTo(membership.Id);
        await Assert.That(await verify.Contexts.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.People.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [ClassDataSource<ProductionSqlServerFixture>]
    public async Task MigrateAsync_FreshSqlServer_AppliesEveryMigrationInOrder(ProductionSqlServerFixture sql)
    {
        await using AsyncServiceScope scope = sql.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        string[] available = db.Database.GetMigrations().ToArray();

        await Assert.That(available).IsNotEmpty();
        await Assert.That(await db.Database.GetAppliedMigrationsAsync()).IsEmpty();

        await db.Database.MigrateAsync();

        string[] applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        await Assert.That(applied).IsEquivalentTo(available, CollectionOrdering.Matching);
        await Assert.That(await db.Database.GetPendingMigrationsAsync()).IsEmpty();
        _ = await db.Churches.AsNoTracking().ToListAsync();
    }

    [Test]
    [ClassDataSource<ProductionSqlServerFixture>]
    public async Task ResetAsync_MigratedSqlServer_PreservesMigrationHistoryAndRemovesSeedRows(ProductionSqlServerFixture sql)
    {
        string[] available;
        await using (AsyncServiceScope scope = sql.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            available = db.Database.GetMigrations().ToArray();
            await Assert.That(available).IsNotEmpty();
            await db.Database.MigrateAsync();

            db.Churches.Add(new Church { Slug = "reset-church", Name = "Reset Church" });
            db.Users.Add(new ApplicationUser { UserName = "reset-user" });
            await db.SaveChangesAsync();
            await Assert.That(await db.Churches.AnyAsync(c => c.Slug == "reset-church")).IsTrue();
            await Assert.That(await db.Users.AnyAsync(u => u.UserName == "reset-user")).IsTrue();
        }

        await sql.ResetAsync();

        await using AsyncServiceScope verifyScope = sql.CreateScope();
        ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await verify.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(available, CollectionOrdering.Matching);
        await Assert.That(await verify.Database.GetPendingMigrationsAsync()).IsEmpty();
        await Assert.That(await verify.Churches.AsNoTracking().ToListAsync()).IsEmpty();
        await Assert.That(await verify.Users.AsNoTracking().ToListAsync()).IsEmpty();
    }
}
