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
    }

    private sealed class TenantRecord : ITenanted
    {
        public Guid Id { get; set; }

        public Guid ChurchId { get; set; }

        public string Name { get; set; } = string.Empty;
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
}
