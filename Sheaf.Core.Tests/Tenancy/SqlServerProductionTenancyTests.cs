using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
        }

        await Sql.ResetAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Read_MembershipQueryFilter_ReturnsOnlyCurrentChurch(bool churchB)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        ChurchMembership expected = churchB ? seed.MembershipB : seed.MembershipA;

        await using AsyncServiceScope scope = Sql.CreateScope(expected.ChurchId);
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ChurchMembership visible = await db.ChurchMemberships.AsNoTracking().SingleAsync();

        await Assert.That(visible.ChurchId).IsEqualTo(expected.ChurchId);
        await Assert.That(visible.Id).IsEqualTo(expected.Id);
        await Assert.That(visible.UserId).IsEqualTo(expected.UserId);
        await Assert.That(await db.ChurchMemberships.IgnoreQueryFilters().CountAsync()).IsEqualTo(2);
        await Assert.That(await db.Churches.CountAsync()).IsEqualTo(2);
        await Assert.That(await db.Users.CountAsync()).IsEqualTo(2);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SaveChanges_BypassLoadedOtherChurchMembership_RejectsModificationOrDeletion(bool delete, bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);

        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
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
        }

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    public async Task SaveChangesAsync_AddMembershipForAnotherChurch_RejectsWrite()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);

        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChurchMemberships.Add(new ChurchMembership
            {
                ChurchId = seed.ChurchB.Id,
                UserId = seed.UserA.Id,
                Role = ChurchMembershipRole.Coordinator
            });

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>()
                .WithMessageContaining("not the current church");
        }

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    public async Task ToListAsync_NoCurrentChurch_RejectsMembershipQuery()
    {
        _ = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using AsyncServiceScope scope = Sql.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.That(async () => await db.ChurchMemberships.ToListAsync())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("A church tenant is required");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChanges_NoCurrentChurch_RejectsMembershipInsert(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChurchMemberships.Add(new ChurchMembership { ChurchId = seed.ChurchA.Id, UserId = seed.UserB.Id });

            await Assert.That(async () => await SaveAsync(db, useAsync))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("A church tenant is required");
        }

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChanges_AddMembershipWithoutChurchId_StampsCurrentChurchId(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        ChurchMembership added = new() { UserId = seed.UserB.Id, Role = ChurchMembershipRole.Coordinator };
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChurchMemberships.Add(added);
            await SaveAsync(db, useAsync);

            await Assert.That(added.ChurchId).IsEqualTo(seed.ChurchA.Id);
        }

        await using AsyncServiceScope verifyScope = Sql.CreateScope(seed.ChurchA.Id);
        ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ChurchMembership persisted = await verify.ChurchMemberships.AsNoTracking().SingleAsync(membership => membership.Id == added.Id);
        await Assert.That(persisted.ChurchId).IsEqualTo(seed.ChurchA.Id);
        await Assert.That(persisted.UserId).IsEqualTo(seed.UserB.Id);
        await Assert.That(persisted.Role).IsEqualTo(ChurchMembershipRole.Coordinator);
        await Assert.That(await verify.ChurchMemberships.IgnoreQueryFilters().CountAsync()).IsEqualTo(3);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChanges_ModifiedMembershipOwnership_RejectsWrite(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ChurchMembership membership = await db.ChurchMemberships.SingleAsync();
            membership.ChurchId = seed.ChurchB.Id;
            membership.Role = ChurchMembershipRole.Admin;

            await Assert.That(async () => await SaveAsync(db, useAsync)).Throws<InvalidOperationException>();
        }

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChanges_CurrentChurchMembership_PersistsPayloadChangeAndDeletion(bool useAsync)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ChurchMembership membership = await db.ChurchMemberships.SingleAsync();
            membership.Role = ChurchMembershipRole.Admin;
            await SaveAsync(db, useAsync);
        }

        await seed.AssertMembershipsAsync(Sql, roleA: ChurchMembershipRole.Admin);

        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChurchMemberships.Remove(await db.ChurchMemberships.SingleAsync());
            await SaveAsync(db, useAsync);
        }

        await using AsyncServiceScope verifyScope = Sql.CreateScope();
        ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ChurchMembership remaining = await verify.ChurchMemberships.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        await Assert.That(remaining.Id).IsEqualTo(seed.MembershipB.Id);
        await Assert.That(remaining.ChurchId).IsEqualTo(seed.ChurchB.Id);
        await Assert.That(remaining.Role).IsEqualTo(ChurchMembershipRole.Leader);
    }

    [Test]
    public async Task SaveChangesAsync_SameUserAcrossChurches_AllowsMembershipButRejectsDuplicateWithinChurch()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChurchMemberships.Add(new ChurchMembership { ChurchId = seed.ChurchA.Id, UserId = seed.UserB.Id });
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchB.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChurchMemberships.Add(new ChurchMembership { ChurchId = seed.ChurchB.Id, UserId = seed.UserB.Id });

            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        }

        await using AsyncServiceScope verifyScope = Sql.CreateScope();
        ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await verify.ChurchMemberships.IgnoreQueryFilters()
            .Where(membership => membership.UserId == seed.UserB.Id)
            .Select(membership => membership.ChurchId).ToArrayAsync())
            .IsEquivalentTo([seed.ChurchA.Id, seed.ChurchB.Id]);
    }

    [Test]
    public async Task SaveChangesAsync_DuplicateChurchSlug_RejectsInsert()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Churches.Add(new Church { Slug = seed.ChurchA.Slug, Name = "Duplicate" });

            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        }

        await using AsyncServiceScope verifyScope = Sql.CreateScope();
        ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await verify.Churches.Select(church => church.Id).ToArrayAsync())
            .IsEquivalentTo([seed.ChurchA.Id, seed.ChurchB.Id]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChangesAsync_OrphanMembershipPrincipal_RejectsWithForeignKeyViolation(bool orphanChurch)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        Guid churchId = orphanChurch ? Guid.NewGuid() : seed.ChurchA.Id;
        await using (AsyncServiceScope scope = Sql.CreateScope(churchId))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChurchMemberships.Add(new ChurchMembership
            {
                ChurchId = churchId,
                UserId = orphanChurch ? seed.UserA.Id : "missing-user"
            });

            DbUpdateException? exception = await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
            await Assert.That((exception?.InnerException as SqlException)?.Number).IsEqualTo(547);
        }

        await seed.AssertMembershipsAsync(Sql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChangesAsync_MembershipPrincipalDeletion_RejectsWithForeignKeyViolation(bool deleteChurch)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
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
        }

        await seed.AssertMembershipsAsync(Sql);
        await using AsyncServiceScope verifyScope = Sql.CreateScope();
        ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await verify.Churches.CountAsync()).IsEqualTo(2);
        await Assert.That(await verify.Users.CountAsync()).IsEqualTo(2);
    }

    private static Task<int> SaveAsync(ApplicationDbContext db, bool useAsync) =>
        useAsync ? db.SaveChangesAsync() : Task.FromResult(db.SaveChanges());
}
