using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Sheaf.Data;

namespace Sheaf.Core.Tests;

[NotInParallel("sql-server")]
public class SqlServerProductionTenancyTests
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
    public async Task Read_MembershipQueryFilter_ReturnsOnlyCurrentChurch(bool churchB)
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);
        ChurchMembership expected = churchB ? seed.MembershipB : seed.MembershipA;

        await Sql.InDatabaseScopeAsync(expected.ChurchId, async db =>
        {
            ChurchMembership visible = await db.ChurchMemberships.AsNoTracking().SingleAsync();

            await Assert.That(visible.ChurchId).IsEqualTo(expected.ChurchId);
            await Assert.That(visible.Id).IsEqualTo(expected.Id);
            await Assert.That(visible.UserId).IsEqualTo(expected.UserId);
            await Assert.That(await db.ChurchMemberships.IgnoreQueryFilters().CountAsync()).IsEqualTo(2);
            await Assert.That(await db.Churches.CountAsync()).IsEqualTo(2);
            await Assert.That(await db.Users.CountAsync()).IsEqualTo(2);
        });
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SaveChanges_BypassLoadedOtherChurchMembership_RejectsModificationOrDeletion(bool delete, bool useAsync)
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);

        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            ChurchMembership membership = await db.ChurchMemberships.IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.ChurchId == seed.ChurchB.Id);
            if (delete)
            {
                db.ChurchMemberships.Remove(membership);
            }
            else
            {
                membership.Role = ChurchMembershipRole.Admin;
            }

            await Assert.That(async () => await SaveAsync(db, useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("not the current church");
        });

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    public async Task SaveChangesAsync_AddMembershipForAnotherChurch_RejectsWrite()
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);

        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            db.ChurchMemberships.Add(new ChurchMembership
            {
                ChurchId = seed.ChurchB.Id,
                UserId = seed.UserA.Id,
                Role = ChurchMembershipRole.Coordinator
            });

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>()
                .WithMessageContaining("not the current church");
        });

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    public async Task ToListAsync_NoCurrentChurch_RejectsMembershipQuery()
    {
        _ = await TenancyTestData.SeedTwoChurchesAsync(Sql);

        await Sql.InDatabaseScopeAsync(null, async db =>
            await Assert.That(async () => await db.ChurchMemberships.ToListAsync())
                .Throws<InvalidOperationException>()
                .WithMessageContaining("A church tenant is required"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChanges_NoCurrentChurch_RejectsMembershipInsert(bool useAsync)
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(null, async db =>
        {
            db.ChurchMemberships.Add(new ChurchMembership { ChurchId = seed.ChurchA.Id, UserId = seed.UserB.Id });

            await Assert.That(async () => await SaveAsync(db, useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("A church tenant is required");
        });

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChanges_AddMembershipWithoutChurchId_StampsCurrentChurchId(bool useAsync)
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);
        ChurchMembership added = new() { UserId = seed.UserB.Id, Role = ChurchMembershipRole.Coordinator };
        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            db.ChurchMemberships.Add(added);
            await SaveAsync(db, useAsync);

            await Assert.That(added.ChurchId).IsEqualTo(seed.ChurchA.Id);
        });

        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async verify =>
        {
            ChurchMembership persisted = await verify.ChurchMemberships.AsNoTracking()
                .SingleAsync(membership => membership.Id == added.Id);
            await Assert.That(persisted.ChurchId).IsEqualTo(seed.ChurchA.Id);
            await Assert.That(persisted.UserId).IsEqualTo(seed.UserB.Id);
            await Assert.That(persisted.Role).IsEqualTo(ChurchMembershipRole.Coordinator);
            await Assert.That(await verify.ChurchMemberships.IgnoreQueryFilters().CountAsync()).IsEqualTo(3);
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChanges_ModifiedMembershipOwnership_RejectsWrite(bool useAsync)
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            ChurchMembership membership = await db.ChurchMemberships.SingleAsync();
            membership.ChurchId = seed.ChurchB.Id;
            membership.Role = ChurchMembershipRole.Admin;

            await Assert.That(async () => await SaveAsync(db, useAsync)).Throws<InvalidOperationException>();
        });

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChanges_CurrentChurchMembership_PersistsPayloadChangeAndDeletion(bool useAsync)
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            ChurchMembership membership = await db.ChurchMemberships.SingleAsync();
            membership.Role = ChurchMembershipRole.Admin;
            await SaveAsync(db, useAsync);
        });

        await seed.AssertMembershipsAsync(Sql, roleA: ChurchMembershipRole.Admin);

        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            db.ChurchMemberships.Remove(await db.ChurchMemberships.SingleAsync());
            await SaveAsync(db, useAsync);
        });

        ChurchMembership remaining = await Sql.InDatabaseScopeAsync(null,
            db => db.ChurchMemberships.IgnoreQueryFilters().AsNoTracking().SingleAsync());
        await Assert.That(remaining.Id).IsEqualTo(seed.MembershipB.Id);
        await Assert.That(remaining.ChurchId).IsEqualTo(seed.ChurchB.Id);
        await Assert.That(remaining.Role).IsEqualTo(ChurchMembershipRole.Leader);
    }

    [Test]
    public async Task SaveChangesAsync_SameUserAcrossChurches_AllowsMembershipButRejectsDuplicateWithinChurch()
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(seed.ChurchA.Id, async db =>
        {
            db.ChurchMemberships.Add(new ChurchMembership { ChurchId = seed.ChurchA.Id, UserId = seed.UserB.Id });
            await db.SaveChangesAsync();
        });

        await Sql.InDatabaseScopeAsync(seed.ChurchB.Id, async db =>
        {
            db.ChurchMemberships.Add(new ChurchMembership { ChurchId = seed.ChurchB.Id, UserId = seed.UserB.Id });

            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        });

        Guid[] userBChurchIds = await Sql.InDatabaseScopeAsync(null, db => db.ChurchMemberships.IgnoreQueryFilters()
            .Where(membership => membership.UserId == seed.UserB.Id)
            .Select(membership => membership.ChurchId).ToArrayAsync());
        await Assert.That(userBChurchIds).IsEquivalentTo([seed.ChurchA.Id, seed.ChurchB.Id]);
    }

    [Test]
    public async Task SaveChangesAsync_DuplicateChurchSlug_RejectsInsert()
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(null, async db =>
        {
            db.Churches.Add(new Church { Slug = seed.ChurchA.Slug, Name = "Duplicate" });

            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        });

        Guid[] churchIds = await Sql.InDatabaseScopeAsync(null, db => db.Churches.Select(church => church.Id).ToArrayAsync());
        await Assert.That(churchIds).IsEquivalentTo([seed.ChurchA.Id, seed.ChurchB.Id]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChangesAsync_OrphanMembershipPrincipal_RejectsWithForeignKeyViolation(bool orphanChurch)
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);
        Guid churchId = orphanChurch ? Guid.NewGuid() : seed.ChurchA.Id;
        await Sql.InDatabaseScopeAsync(churchId, async db =>
        {
            db.ChurchMemberships.Add(new ChurchMembership
            {
                ChurchId = churchId,
                UserId = orphanChurch ? seed.UserA.Id : "missing-user"
            });

            DbUpdateException? exception = await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
            await Assert.That((exception?.InnerException as SqlException)?.Number).IsEqualTo(547);
        });

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChangesAsync_MembershipPrincipalDeletion_RejectsWithForeignKeyViolation(bool deleteChurch)
    {
        TwoChurchScenario seed = await TenancyTestData.SeedTwoChurchesAsync(Sql);
        await Sql.InDatabaseScopeAsync(null, async db =>
        {
            if (deleteChurch)
            {
                db.Churches.Remove(await db.Churches.SingleAsync(church => church.Id == seed.ChurchA.Id));
            }
            else
            {
                db.Users.Remove(await db.Users.SingleAsync(user => user.Id == seed.UserA.Id));
            }

            DbUpdateException? exception = await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
            await Assert.That((exception?.InnerException as SqlException)?.Number).IsEqualTo(547);
        });

        await seed.AssertMembershipsAsync(Sql);
        await Sql.InDatabaseScopeAsync(null, async verify =>
        {
            await Assert.That(await verify.Churches.CountAsync()).IsEqualTo(2);
            await Assert.That(await verify.Users.CountAsync()).IsEqualTo(2);
        });
    }

    private static Task<int> SaveAsync(ApplicationDbContext db, bool useAsync) =>
        useAsync ? db.SaveChangesAsync() : Task.FromResult(db.SaveChanges());
}
