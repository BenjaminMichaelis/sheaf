using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sheaf.Data;
using Sheaf.Data.Tenancy;

using DomainCapture = Sheaf.Data.Capture;
using DomainContext = Sheaf.Data.Context;
using DomainTouchpoint = Sheaf.Data.Touchpoint;

namespace Sheaf.Core.Tests;

/// <summary>
/// Missing-tenant, mismatched-tenant and bypass-loaded cross-church write guards on the migrated production schema.
/// </summary>
public class ProductionTenancyWriteGuardTests
{
    private const string MissingTenant = "A church tenant is required";
    private const string ForeignChurch = "not the current church";
    // EF rejects key changes ("part of a key") before the interceptor's "ChurchId is immutable" check; both name ChurchId.
    private const string ChurchIdChange = "ChurchId";

    [ClassDataSource<ProductionSqlServerFixture>]
    public required ProductionSqlServerFixture Sql { get; init; }

    // Missing tenant: queries

    [Test]
    public async Task ToListAsync_NoCurrentChurch_ThrowsTenantRequired()
    {
        await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using AsyncServiceScope scope = Sql.CreateScope();
        ApplicationDbContext db = Db(scope);

        await Assert.That(async () => await db.Captures.ToListAsync())
            .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
    }

    [Test]
    public async Task ToList_NoCurrentChurch_ThrowsTenantRequired()
    {
        await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using AsyncServiceScope scope = Sql.CreateScope();
        ApplicationDbContext db = Db(scope);

        await Assert.That(() => db.People.ToList())
            .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
    }

    [Test]
    public async Task FindAsync_NoCurrentChurchWithExistingKey_ThrowsTenantRequired()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using AsyncServiceScope scope = Sql.CreateScope();
        ApplicationDbContext db = Db(scope);

        await Assert.That(async () => await db.Captures.FindAsync(seed.ChurchA.Church.Id, seed.ChurchA.Capture.Id))
            .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
    }

    [Test]
    public async Task Find_NoCurrentChurchWithExistingKey_ThrowsTenantRequired()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using AsyncServiceScope scope = Sql.CreateScope();
        ApplicationDbContext db = Db(scope);

        await Assert.That(() => db.Captures.Find(seed.ChurchA.Church.Id, seed.ChurchA.Capture.Id))
            .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
    }

    [Test]
    public async Task AnyAsync_NoCurrentChurch_ThrowsTenantRequired()
    {
        await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using AsyncServiceScope scope = Sql.CreateScope();
        ApplicationDbContext db = Db(scope);

        await Assert.That(async () => await db.Touchpoints.AnyAsync())
            .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
    }

    // Missing tenant: SaveChanges inserts, updates and deletes

    [Test]
    public async Task SaveChangesAsync_NoCurrentChurchAddWithoutChurchId_ThrowsAndWritesNothing()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        DomainCapture capture = new() { Status = "missing-tenant-insert" };
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Add(capture);

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await Assert.That(capture.ChurchId).IsEqualTo(Guid.Empty);
        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChanges_NoCurrentChurchAddWithoutChurchId_ThrowsAndWritesNothing()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        DomainCapture capture = new() { Status = "missing-tenant-insert" };
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Add(capture);

            await Assert.That(() => db.SaveChanges())
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await Assert.That(capture.ChurchId).IsEqualTo(Guid.Empty);
        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_NoCurrentChurchAddWithExplicitChurchId_ThrowsAndWritesNothing()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Add(new DomainCapture { ChurchId = seed.ChurchA.Church.Id, Status = "missing-tenant-explicit" });

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_NoCurrentChurchUpdateAttachedEntity_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Update(DetachedCapture(db, seed.ChurchA, "missing-tenant-update"));

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChanges_NoCurrentChurchUpdateAttachedEntity_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Update(DetachedCapture(db, seed.ChurchA, "missing-tenant-update"));

            await Assert.That(() => db.SaveChanges())
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_NoCurrentChurchRemoveAttachedEntity_ThrowsAndKeepsRow()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Remove(DetachedCapture(db, seed.ChurchA, seed.ChurchA.Capture.Status));

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChanges_NoCurrentChurchRemoveAttachedEntity_ThrowsAndKeepsRow()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Remove(DetachedCapture(db, seed.ChurchA, seed.ChurchA.Capture.Status));

            await Assert.That(() => db.SaveChanges())
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_NoCurrentChurchModifyBypassLoadedEntity_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);
            DomainCapture loaded = await db.Captures.IgnoreQueryFilters([MultiTenancyConvention.FilterName])
                .SingleAsync(capture => capture.Id == seed.ChurchB.Capture.Id);
            loaded.Status = "missing-tenant-bypass-update";

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_NoCurrentChurchRemoveBypassLoadedEntity_ThrowsAndKeepsRow()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);
            DomainCapture loaded = await db.Captures.IgnoreQueryFilters([MultiTenancyConvention.FilterName])
                .SingleAsync(capture => capture.Id == seed.ChurchB.Capture.Id);
            db.Captures.Remove(loaded);

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    // Missing tenant: bulk operations

    [Test]
    public async Task ExecuteUpdateAsync_NoCurrentChurch_ThrowsAndLeavesRowsUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);

            await Assert.That(async () => await db.Captures.ExecuteUpdateAsync(
                    setters => setters.SetProperty(capture => capture.Status, "missing-tenant-bulk")))
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task ExecuteUpdate_NoCurrentChurch_ThrowsAndLeavesRowsUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);

            await Assert.That(() => db.Captures.ExecuteUpdate(
                    setters => setters.SetProperty(capture => capture.Status, "missing-tenant-bulk")))
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task ExecuteDeleteAsync_NoCurrentChurch_ThrowsAndKeepsRows()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);

            await Assert.That(async () => await db.Captures.ExecuteDeleteAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task ExecuteDelete_NoCurrentChurch_ThrowsAndKeepsRows()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope())
        {
            ApplicationDbContext db = Db(scope);

            await Assert.That(() => db.Captures.ExecuteDelete())
                .Throws<InvalidOperationException>().WithMessageContaining(MissingTenant);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    // Mismatched-tenant inserts

    [Test]
    public async Task SaveChangesAsync_AddWithOtherChurchId_ThrowsAndWritesNothing()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Add(new DomainCapture { ChurchId = seed.ChurchB.Church.Id, Status = "mismatched-insert" });

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChanges_AddWithOtherChurchId_ThrowsAndWritesNothing()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Add(new DomainCapture { ChurchId = seed.ChurchB.Church.Id, Status = "mismatched-insert" });

            await Assert.That(() => db.SaveChanges())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_AddWithUnknownChurchId_ThrowsBeforeDatabaseWrite()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Add(new DomainCapture { ChurchId = Guid.NewGuid(), Status = "unknown-church-insert" });

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_AddRangeWithOneOtherChurchEntity_ThrowsAndWritesNeither()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.AddRange(
                new DomainCapture { Status = "valid-current-church-insert" },
                new DomainCapture { ChurchId = seed.ChurchB.Church.Id, Status = "mismatched-batch-insert" });

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_AddChildConsistentWithOtherChurchParents_ThrowsAndWritesNothing()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            db.Touchpoints.Add(new DomainTouchpoint
            {
                ChurchId = seed.ChurchB.Church.Id,
                PersonId = seed.ChurchB.Person.Id,
                AuthorLeaderId = seed.ChurchB.Membership.Id,
                ContextId = seed.ChurchB.Context.Id,
                Note = "Synthetic church B child written from church A",
                NoteVisibility = NoteVisibility.Private,
                OccurredAt = ProductionTwoChurchSeed.OccurredAt
            });

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalTouchpointsAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_AddUnstampedChildReferencingOtherChurchParent_ThrowsForeignKeyViolation()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            db.ContextMembers.Add(new ContextMember
            {
                ContextId = seed.ChurchB.Context.Id,
                PersonId = seed.ChurchA.Person.Id,
                StartedOn = ProductionTwoChurchSeed.OccurredAt
            });

            DbUpdateException? exception = await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<DbUpdateException>();
            SqlException? sql = exception?.InnerException as SqlException;
            await Assert.That(sql).IsNotNull();
            await Assert.That(sql?.Number).IsEqualTo(547);
            await Assert.That(sql?.Message).Contains("FK_ContextMembers_Contexts_ChurchId_ContextId");
        }

        foreach (ProductionChurchSeed church in new[] { seed.ChurchA, seed.ChurchB })
        {
            await using AsyncServiceScope scope = Sql.CreateScope(church.Church.Id);
            List<ContextMember> members = await Db(scope).ContextMembers.AsNoTracking().ToListAsync();
            await Assert.That(members.Select(member => member.Id)).IsEquivalentTo([church.ContextMember.Id]);
        }
    }

    // Bypass-loaded cross-church modifications and deletions

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SaveChangesAsync_ModifyBypassLoadedOtherChurchEntity_ThrowsAndLeavesRowUnchanged(bool namedFilterBypass)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            DomainCapture loaded = await LoadOtherChurchCaptureAsync(db, seed, namedFilterBypass);
            loaded.Status = "cross-church-update";

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SaveChanges_ModifyBypassLoadedOtherChurchEntity_ThrowsAndLeavesRowUnchanged(bool namedFilterBypass)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            DomainCapture loaded = await LoadOtherChurchCaptureAsync(db, seed, namedFilterBypass);
            loaded.Status = "cross-church-update";

            await Assert.That(() => db.SaveChanges())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SaveChangesAsync_RemoveBypassLoadedOtherChurchEntity_ThrowsAndKeepsRow(bool namedFilterBypass)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Remove(await LoadOtherChurchCaptureAsync(db, seed, namedFilterBypass));

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SaveChanges_RemoveBypassLoadedOtherChurchEntity_ThrowsAndKeepsRow(bool namedFilterBypass)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Remove(await LoadOtherChurchCaptureAsync(db, seed, namedFilterBypass));

            await Assert.That(() => db.SaveChanges())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_ModifyForeignKeyOnBypassLoadedOtherChurchChild_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            DomainTouchpoint loaded = await db.Touchpoints.IgnoreQueryFilters([MultiTenancyConvention.FilterName])
                .SingleAsync(touchpoint => touchpoint.Id == seed.ChurchB.Touchpoint.Id);
            loaded.ContextId = null;
            loaded.Note = "cross-church child update";

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalTouchpointsAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_ModifyOwnedJsonOnBypassLoadedOtherChurchEntity_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);

        // Report the persisted row even when no exception is thrown.
        using (Assert.Multiple())
        {
            await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
            {
                ApplicationDbContext db = Db(scope);
                DomainContext loaded = await db.Contexts.IgnoreQueryFilters([MultiTenancyConvention.FilterName])
                    .SingleAsync(context => context.Id == seed.ChurchB.Context.Id);
                loaded.CustomFieldSchema.Fields[0].Type = "cross-church-json-update";

                await Assert.That(async () => await db.SaveChangesAsync())
                    .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
            }

            await AssertOriginalContextsAsync(seed);
        }
    }

    [Test]
    public async Task SaveChangesAsync_ReplaceOwnedJsonOnBypassLoadedOtherChurchEntity_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);

        // Report the persisted row even when no exception is thrown.
        using (Assert.Multiple())
        {
            await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
            {
                ApplicationDbContext db = Db(scope);
                DomainContext loaded = await db.Contexts.IgnoreQueryFilters([MultiTenancyConvention.FilterName])
                    .SingleAsync(context => context.Id == seed.ChurchB.Context.Id);
                loaded.CustomFieldSchema = new CustomFieldSchema
                {
                    Fields = [new CustomFieldDefinition { Name = "cross-church-json-replace", Type = "text" }]
                };

                await Assert.That(async () => await db.SaveChangesAsync())
                    .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
            }

            await AssertOriginalContextsAsync(seed);
        }
    }

    [Test]
    public async Task SaveChanges_ModifyOwnedJsonOnBypassLoadedOtherChurchEntity_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);

        // Report the persisted row even when no exception is thrown.
        using (Assert.Multiple())
        {
            await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
            {
                ApplicationDbContext db = Db(scope);
                DomainContext loaded = db.Contexts.IgnoreQueryFilters([MultiTenancyConvention.FilterName])
                    .Single(context => context.Id == seed.ChurchB.Context.Id);
                loaded.CustomFieldSchema.Fields[0].Type = "cross-church-json-update";

                await Assert.That(() => db.SaveChanges())
                    .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
            }

            await AssertOriginalContextsAsync(seed);
        }
    }

    [Test]
    public async Task SaveChanges_ReplaceOwnedJsonOnBypassLoadedOtherChurchEntity_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);

        // Report the persisted row even when no exception is thrown.
        using (Assert.Multiple())
        {
            await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
            {
                ApplicationDbContext db = Db(scope);
                DomainContext loaded = db.Contexts.IgnoreQueryFilters([MultiTenancyConvention.FilterName])
                    .Single(context => context.Id == seed.ChurchB.Context.Id);
                loaded.CustomFieldSchema = new CustomFieldSchema
                {
                    Fields = [new CustomFieldDefinition { Name = "cross-church-json-replace", Type = "text" }]
                };

                await Assert.That(() => db.SaveChanges())
                    .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
            }

            await AssertOriginalContextsAsync(seed);
        }
    }

    [Test]
    public async Task SaveChangesAsync_ChangeChurchIdOfBypassLoadedOtherChurchEntityToCurrent_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            DomainCapture loaded = await LoadOtherChurchCaptureAsync(db, seed, namedFilterBypass: true);
            loaded.ChurchId = seed.ChurchA.Church.Id;

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(ChurchIdChange);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_ChangeChurchIdOfTrackedCurrentChurchEntity_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            DomainCapture loaded = await db.Captures.SingleAsync(capture => capture.Id == seed.ChurchA.Capture.Id);
            loaded.ChurchId = seed.ChurchB.Church.Id;

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(ChurchIdChange);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChanges_ChangeChurchIdOfTrackedCurrentChurchEntity_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            DomainCapture loaded = db.Captures.Single(capture => capture.Id == seed.ChurchA.Capture.Id);
            loaded.ChurchId = seed.ChurchB.Church.Id;

            await Assert.That(() => db.SaveChanges())
                .Throws<InvalidOperationException>().WithMessageContaining(ChurchIdChange);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_UpdateDetachedOtherChurchEntity_ThrowsAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Update(DetachedCapture(db, seed.ChurchB, "detached-cross-church-update"));

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_RemoveDetachedOtherChurchEntity_ThrowsAndKeepsRow()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            db.Captures.Remove(DetachedCapture(db, seed.ChurchB, seed.ChurchB.Capture.Status));

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>().WithMessageContaining(ForeignChurch);
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_UpdateOtherChurchIdUnderCurrentChurchId_ThrowsConcurrencyAndLeavesRowUnchanged()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            DomainCapture spoof = DetachedCapture(db, seed.ChurchB, "spoofed-key-update");
            spoof.ChurchId = seed.ChurchA.Church.Id;
            db.Captures.Update(spoof);

            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_RemoveOtherChurchIdUnderCurrentChurchId_ThrowsConcurrencyAndKeepsRow()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            DomainCapture spoof = DetachedCapture(db, seed.ChurchB, seed.ChurchB.Capture.Status);
            spoof.ChurchId = seed.ChurchA.Church.Id;
            db.Captures.Remove(spoof);

            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        }

        await AssertOriginalCapturesAsync(seed);
    }

    [Test]
    public async Task SaveChangesAsync_ModifyOwnedJsonOnCurrentChurchEntity_PersistsChange()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        const string updatedType = "current-church-json-update";
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = Db(scope);
            DomainContext loaded = await db.Contexts.SingleAsync(context => context.Id == seed.ChurchA.Context.Id);
            loaded.CustomFieldSchema.Fields[0].Type = updatedType;
            await Assert.That(await db.SaveChangesAsync()).IsGreaterThan(0);
        }

        await using AsyncServiceScope verifyScope = Sql.CreateScope(seed.ChurchA.Church.Id);
        DomainContext persisted = await Db(verifyScope).Contexts.AsNoTracking()
            .SingleAsync(context => context.Id == seed.ChurchA.Context.Id);
        await Assert.That(persisted.CustomFieldSchema.Fields[0].Type).IsEqualTo(updatedType);
    }

    private static ApplicationDbContext Db(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    private static Task<DomainCapture> LoadOtherChurchCaptureAsync(
        ApplicationDbContext db, ProductionTwoChurchSeed seed, bool namedFilterBypass)
    {
        IQueryable<DomainCapture> bypass = namedFilterBypass
            ? db.Captures.IgnoreQueryFilters([MultiTenancyConvention.FilterName])
            : db.Captures.IgnoreQueryFilters();
        return bypass.SingleAsync(capture => capture.Id == seed.ChurchB.Capture.Id);
    }

    // Recreates a church's seeded capture key without loading it, as a caller holding stale or forged data would.
    private static DomainCapture DetachedCapture(ApplicationDbContext db, ProductionChurchSeed church, string status)
    {
        DomainCapture capture = new()
        {
            ChurchId = church.Church.Id,
            RawText = church.Capture.RawText,
            Transcript = church.Capture.Transcript,
            ExtractionJson = church.Capture.ExtractionJson,
            Status = status
        };
        db.Entry(capture).Property(row => row.Id).CurrentValue = church.Capture.Id;
        return capture;
    }

    private async Task AssertOriginalCapturesAsync(ProductionTwoChurchSeed seed)
    {
        foreach (ProductionChurchSeed church in new[] { seed.ChurchA, seed.ChurchB })
        {
            await using AsyncServiceScope scope = Sql.CreateScope(church.Church.Id);
            List<DomainCapture> rows = await Db(scope).Captures.AsNoTracking().ToListAsync();

            await Assert.That(rows).Count().IsEqualTo(1);
            DomainCapture persisted = rows[0];
            await Assert.That(persisted.Id).IsEqualTo(church.Capture.Id);
            await Assert.That(persisted.ChurchId).IsEqualTo(church.Church.Id);
            await Assert.That(persisted.Status).IsEqualTo(church.Capture.Status);
            await Assert.That(persisted.RawText).IsEqualTo(church.Capture.RawText);
        }

        await AssertTotalRowsAsync<DomainCapture>(2);
    }

    private async Task AssertOriginalTouchpointsAsync(ProductionTwoChurchSeed seed)
    {
        foreach (ProductionChurchSeed church in new[] { seed.ChurchA, seed.ChurchB })
        {
            await using AsyncServiceScope scope = Sql.CreateScope(church.Church.Id);
            List<DomainTouchpoint> rows = await Db(scope).Touchpoints.AsNoTracking().ToListAsync();

            await Assert.That(rows).Count().IsEqualTo(1);
            DomainTouchpoint persisted = rows[0];
            await Assert.That(persisted.Id).IsEqualTo(church.Touchpoint.Id);
            await Assert.That(persisted.ChurchId).IsEqualTo(church.Church.Id);
            await Assert.That(persisted.ContextId).IsEqualTo(church.Context.Id);
            await Assert.That(persisted.Note).IsEqualTo(church.Touchpoint.Note);
        }

        await AssertTotalRowsAsync<DomainTouchpoint>(2);
    }

    private async Task AssertOriginalContextsAsync(ProductionTwoChurchSeed seed)
    {
        foreach (ProductionChurchSeed church in new[] { seed.ChurchA, seed.ChurchB })
        {
            await using AsyncServiceScope scope = Sql.CreateScope(church.Church.Id);
            DomainContext persisted = await Db(scope).Contexts.AsNoTracking().SingleAsync();

            await Assert.That(persisted.Id).IsEqualTo(church.Context.Id);
            await Assert.That(persisted.ChurchId).IsEqualTo(church.Church.Id);
            await Assert.That(persisted.CustomFieldSchema.Fields.Select(field => (field.Name, field.Type)))
                .IsEquivalentTo(church.Context.CustomFieldSchema.Fields.Select(field => (field.Name, field.Type)));
        }
    }

    // Counts across every church so a stray row written under an unexpected ChurchId cannot hide behind the filter.
    private async Task AssertTotalRowsAsync<TEntity>(int expected)
        where TEntity : class, ITenanted
    {
        await using AsyncServiceScope scope = Sql.CreateScope();
        int total = await Db(scope).Set<TEntity>().IgnoreQueryFilters([MultiTenancyConvention.FilterName]).CountAsync();
        await Assert.That(total).IsEqualTo(expected);
    }
}
