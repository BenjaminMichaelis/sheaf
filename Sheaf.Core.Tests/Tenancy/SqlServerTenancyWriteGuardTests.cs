using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

[NotInParallel("sql-server")]
public class SqlServerTenancyWriteGuardTests
{
    [ClassDataSource<SqlServerFixture>(Shared = SharedType.PerAssembly)]
    public required SqlServerFixture Sql { get; init; }

    [Before(Test)]
    public async Task ResetDatabaseAsync() => await Sql.ResetAsync();

    [Test]
    public async Task SaveChangesAsync_NoCurrentChurchAdd_ThrowsAndWritesNothing()
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);
        await using (TenancyTestDbContext db = Sql.CreateContext(null))
        {
            db.Set<GuardRecord>().Add(new GuardRecord { Name = "missing-tenant" });
            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>()
                .WithMessageContaining("A church tenant is required");
        }

        await using TenancyTestDbContext verify = Sql.CreateContext(seed.ChurchA.Id);
        await Assert.That(await verify.Set<GuardRecord>().IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task SaveChangesAsync_AddForAnotherChurch_ThrowsAndWritesNothing()
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);
        await using (TenancyTestDbContext db = Sql.CreateContext(seed.ChurchA.Id))
        {
            db.Set<GuardRecord>().Add(new GuardRecord
            {
                ChurchId = seed.ChurchB.Id,
                Name = "mismatched-tenant"
            });

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>()
                .WithMessageContaining("not the current church");
        }

        await using TenancyTestDbContext verify = Sql.CreateContext(seed.ChurchB.Id);
        await Assert.That(await verify.Set<GuardRecord>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SaveChangesAsync_BypassLoadedOtherChurchEntity_RejectsModificationOrDeletion(bool delete)
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);
        GuardRecord record = new()
        {
            ChurchId = seed.ChurchB.Id,
            Name = "other-church",
            Details = new GuardRecordDetails { Label = "original", Items = [new GuardRecordItem { Value = "original" }] }
        };
        await using (TenancyTestDbContext seedContext = Sql.CreateContext(seed.ChurchB.Id))
        {
            seedContext.Set<GuardRecord>().Add(record);
            await seedContext.SaveChangesAsync();
        }

        await using (TenancyTestDbContext db = Sql.CreateContext(seed.ChurchA.Id))
        {
            GuardRecord loaded = await db.Set<GuardRecord>().IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.Id == record.Id);
            if (delete)
            {
                db.Set<GuardRecord>().Remove(loaded);
            }
            else
            {
                loaded.Name = "changed-from-other-church";
            }

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>()
                .WithMessageContaining("not the current church");
        }

        await using TenancyTestDbContext verify = Sql.CreateContext(seed.ChurchB.Id);
        GuardRecord persisted = await verify.Set<GuardRecord>().AsNoTracking().SingleAsync();
        await Assert.That(persisted.Name).IsEqualTo("other-church");
        await Assert.That(persisted.Details.Label).IsEqualTo("original");
    }

    [Test]
    public async Task SaveChangesAsync_BypassLoadedOtherChurchOwnedJson_RejectsModificationAndPreservesRow()
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);
        GuardRecord record = new()
        {
            ChurchId = seed.ChurchB.Id,
            Name = "owned-json",
            Details = new GuardRecordDetails { Label = "original", Items = [new GuardRecordItem { Value = "original" }] }
        };
        await using (TenancyTestDbContext seedContext = Sql.CreateContext(seed.ChurchB.Id))
        {
            seedContext.Set<GuardRecord>().Add(record);
            await seedContext.SaveChangesAsync();
        }

        await using (TenancyTestDbContext db = Sql.CreateContext(seed.ChurchA.Id))
        {
            GuardRecord loaded = await db.Set<GuardRecord>().IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.Id == record.Id);
            loaded.Details.Items[0].Value = "cross-church-change";

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>()
                .WithMessageContaining("not the current church");
        }

        await using TenancyTestDbContext verify = Sql.CreateContext(seed.ChurchB.Id);
        GuardRecord persisted = await verify.Set<GuardRecord>().AsNoTracking().SingleAsync();
        await Assert.That(persisted.Details.Items[0].Value).IsEqualTo("original");
    }

    [Test]
    public async Task SaveChangesAsync_SqlServerFixtureOrphanChurchId_ThrowsForeignKeyViolation()
    {
        Guid orphanChurchId = Guid.NewGuid();
        await using TenancyTestDbContext db = Sql.CreateContext(orphanChurchId);
        db.Set<GuardRecord>().Add(new GuardRecord { ChurchId = orphanChurchId, Name = "orphan" });

        DbUpdateException? exception = await Assert.That(async () => await db.SaveChangesAsync())
            .Throws<DbUpdateException>();
        await Assert.That((exception?.InnerException as SqlException)?.Number).IsEqualTo(547);
    }

    [Test]
    public async Task SaveChangesAsync_CurrentChurchOwnedJson_PersistsChange()
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);
        GuardRecord record = new()
        {
            ChurchId = seed.ChurchA.Id,
            Name = "same-church-json",
            Details = new GuardRecordDetails { Label = "original", Items = [new GuardRecordItem { Value = "original" }] }
        };
        await using (TenancyTestDbContext db = Sql.CreateContext(seed.ChurchA.Id))
        {
            db.Set<GuardRecord>().Add(record);
            await db.SaveChangesAsync();
            record.Details.Label = "updated";
            await db.SaveChangesAsync();
        }

        await using TenancyTestDbContext verify = Sql.CreateContext(seed.ChurchA.Id);
        GuardRecord persisted = await verify.Set<GuardRecord>().AsNoTracking().SingleAsync();
        await Assert.That(persisted.Details.Label).IsEqualTo("updated");
    }
}
