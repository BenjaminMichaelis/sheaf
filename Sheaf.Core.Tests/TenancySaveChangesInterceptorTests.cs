using Microsoft.EntityFrameworkCore;

using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

public class TenancySaveChangesInterceptorTests
{
    private sealed class TestContext(DbContextOptions<TestContext> options, Guid churchId)
        : DbContext(options), ITenantContext
    {
        public Guid ChurchId { get; } = churchId;

        public DbSet<TenantRecord> TenantRecords => Set<TenantRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<TenantRecord>().OwnsOne(
                record => record.Details,
                details => details.OwnsMany(detail => detail.Items));
    }

    private sealed class TenantRecord : ITenanted
    {
        public Guid Id { get; set; }

        public Guid ChurchId { get; set; }

        public string Name { get; set; } = string.Empty;

        public RecordDetails Details { get; set; } = new();
    }

    private sealed class RecordDetails
    {
        public string Label { get; set; } = string.Empty;

        public List<RecordItem> Items { get; set; } = [];
    }

    private sealed class RecordItem
    {
        public string Value { get; set; } = string.Empty;
    }

    private static TestContext CreateContext(string databaseName, Guid churchId) =>
        new(
            new DbContextOptionsBuilder<TestContext>()
                .UseInMemoryDatabase(databaseName)
                .UseMultiTenancy()
                .Options,
            churchId);

    private static TestContext CreateInterceptorOnlyContext(string databaseName, Guid churchId) =>
        new(
            new DbContextOptionsBuilder<TestContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new TenancySaveChangesInterceptor())
                .Options,
            churchId);

    [Test]
    public async Task SaveChangesAsync_AddWithoutChurchId_StampsCurrentChurchId()
    {
        Guid churchId = Guid.NewGuid();
        await using TestContext context = CreateContext(nameof(SaveChangesAsync_AddWithoutChurchId_StampsCurrentChurchId), churchId);
        TenantRecord record = new() { Id = Guid.NewGuid(), Name = "Record" };
        context.TenantRecords.Add(record);

        await context.SaveChangesAsync();

        await Assert.That(record.ChurchId).IsEqualTo(churchId);
    }

    [Test]
    public async Task SaveChangesAsync_AddWithForeignChurchId_Throws()
    {
        Guid churchId = Guid.NewGuid();
        Guid foreignChurchId = Guid.NewGuid();
        await using TestContext context = CreateContext(nameof(SaveChangesAsync_AddWithForeignChurchId_Throws), churchId);
        TenantRecord record = new() { Id = Guid.NewGuid(), ChurchId = foreignChurchId, Name = "Record" };
        context.TenantRecords.Add(record);

        await Assert.That(async () => await context.SaveChangesAsync())
            .Throws<InvalidOperationException>()
            .WithMessage($"The entity belongs to church '{foreignChurchId}', not the current church '{churchId}'.");
    }

    [Test]
    public async Task SaveChangesAsync_ModifyChurchId_Throws()
    {
        Guid churchId = Guid.NewGuid();
        await using TestContext context = CreateInterceptorOnlyContext(nameof(SaveChangesAsync_ModifyChurchId_Throws), churchId);
        TenantRecord record = new() { Id = Guid.NewGuid(), Name = "Record" };
        context.TenantRecords.Add(record);
        await context.SaveChangesAsync();
        record.ChurchId = Guid.NewGuid();

        await Assert.That(async () => await context.SaveChangesAsync())
            .Throws<InvalidOperationException>()
            .WithMessage("ChurchId is immutable after an entity is tracked.");
    }

    [Test]
    public async Task SaveChangesAsync_AttachUpdateForeignEntity_Throws()
    {
        Guid churchId = Guid.NewGuid();
        Guid foreignChurchId = Guid.NewGuid();
        await using TestContext context = CreateContext(nameof(SaveChangesAsync_AttachUpdateForeignEntity_Throws), churchId);
        TenantRecord record = new() { Id = Guid.NewGuid(), ChurchId = foreignChurchId, Name = "Record" };
        context.TenantRecords.Attach(record);
        context.Entry(record).Property(entity => entity.Name).IsModified = true;

        await Assert.That(async () => await context.SaveChangesAsync())
            .Throws<InvalidOperationException>()
            .WithMessage($"The entity belongs to church '{foreignChurchId}', not the current church '{churchId}'.");
    }

    [Test]
    public async Task SaveChangesAsync_DeleteForeignEntity_Throws()
    {
        Guid churchId = Guid.NewGuid();
        Guid foreignChurchId = Guid.NewGuid();
        await using TestContext context = CreateContext(nameof(SaveChangesAsync_DeleteForeignEntity_Throws), churchId);
        TenantRecord record = new() { Id = Guid.NewGuid(), ChurchId = foreignChurchId, Name = "Record" };
        context.TenantRecords.Attach(record);
        context.TenantRecords.Remove(record);

        await Assert.That(async () => await context.SaveChangesAsync())
            .Throws<InvalidOperationException>()
            .WithMessage($"The entity belongs to church '{foreignChurchId}', not the current church '{churchId}'.");
    }

    [Test]
    public async Task SaveChangesAsync_ModifyOwnedOnForeignEntity_Throws()
    {
        Guid churchId = Guid.NewGuid();
        Guid foreignChurchId = Guid.NewGuid();
        await using TestContext context = CreateContext(nameof(SaveChangesAsync_ModifyOwnedOnForeignEntity_Throws), churchId);
        TenantRecord record = RecordWithDetails(foreignChurchId);
        context.TenantRecords.Attach(record);
        record.Details.Label = "cross-church";

        await Assert.That(async () => await context.SaveChangesAsync())
            .Throws<InvalidOperationException>()
            .WithMessage($"The entity belongs to church '{foreignChurchId}', not the current church '{churchId}'.");
    }

    [Test]
    public async Task SaveChanges_ReplaceOwnedOnForeignEntity_Throws()
    {
        Guid churchId = Guid.NewGuid();
        Guid foreignChurchId = Guid.NewGuid();
        using TestContext context = CreateContext(nameof(SaveChanges_ReplaceOwnedOnForeignEntity_Throws), churchId);
        TenantRecord record = RecordWithDetails(foreignChurchId);
        context.TenantRecords.Attach(record);
        record.Details = new RecordDetails { Label = "replacement", Items = [new RecordItem { Value = "new" }] };

        await Assert.That(() => context.SaveChanges())
            .Throws<InvalidOperationException>()
            .WithMessage($"The entity belongs to church '{foreignChurchId}', not the current church '{churchId}'.");
    }

    [Test]
    public async Task SaveChangesAsync_AddNestedOwnedItemOnForeignEntity_Throws()
    {
        Guid churchId = Guid.NewGuid();
        Guid foreignChurchId = Guid.NewGuid();
        await using TestContext context = CreateContext(nameof(SaveChangesAsync_AddNestedOwnedItemOnForeignEntity_Throws), churchId);
        TenantRecord record = RecordWithDetails(foreignChurchId);
        context.TenantRecords.Attach(record);
        record.Details.Items.Add(new RecordItem { Value = "cross-church" });

        await Assert.That(async () => await context.SaveChangesAsync())
            .Throws<InvalidOperationException>()
            .WithMessage($"The entity belongs to church '{foreignChurchId}', not the current church '{churchId}'.");
    }

    [Test]
    public async Task SaveChangesAsync_ModifyOwnedWithoutCurrentChurch_ThrowsTenantRequired()
    {
        await using TestContext context = CreateContext(nameof(SaveChangesAsync_ModifyOwnedWithoutCurrentChurch_ThrowsTenantRequired), Guid.Empty);
        TenantRecord record = RecordWithDetails(Guid.NewGuid());
        context.TenantRecords.Attach(record);
        record.Details.Items[0].Value = "no-tenant";

        await Assert.That(async () => await context.SaveChangesAsync())
            .Throws<InvalidOperationException>()
            .WithMessage("A church tenant is required to save tenanted entities.");
    }

    [Test]
    public async Task SaveChangesAsync_ModifyOwnedOnCurrentChurchEntity_PersistsChange()
    {
        Guid churchId = Guid.NewGuid();
        string databaseName = nameof(SaveChangesAsync_ModifyOwnedOnCurrentChurchEntity_PersistsChange);
        TenantRecord record = RecordWithDetails(churchId);
        await using (TestContext context = CreateContext(databaseName, churchId))
        {
            context.TenantRecords.Add(record);
            await context.SaveChangesAsync();
            record.Details.Items[0].Value = "updated";
            await context.SaveChangesAsync();
        }

        await using TestContext verify = CreateContext(databaseName, churchId);
        TenantRecord persisted = await verify.TenantRecords.AsNoTracking().SingleAsync(row => row.Id == record.Id);
        await Assert.That(persisted.Details.Items[0].Value).IsEqualTo("updated");
    }

    private static TenantRecord RecordWithDetails(Guid churchId) =>
        new()
        {
            Id = Guid.NewGuid(),
            ChurchId = churchId,
            Name = "Record",
            Details = new RecordDetails { Label = "original", Items = [new RecordItem { Value = "original" }] }
        };
}
