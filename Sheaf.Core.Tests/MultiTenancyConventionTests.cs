using Microsoft.EntityFrameworkCore;

using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

public class MultiTenancyConventionTests
{
    private sealed class Parent : ITenanted
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }
        public List<Child> Children { get; set; } = [];
    }
    private sealed class Child : ITenanted
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }
        public Guid? ParentId { get; set; }
        public Parent? Parent { get; set; }
    }

    private sealed class Keyless : ITenanted
    {
        public Guid ChurchId { get; set; }
    }

    private sealed class Untenanted
    {
        public int Id { get; set; }
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options), ITenantContext
    {
        public Guid ChurchId { get; set; }
        public DbSet<Parent> Parents => Set<Parent>();
        public DbSet<Child> Children => Set<Child>();
        public DbSet<Untenanted> Untenanteds => Set<Untenanted>();
    }

    private sealed class KeylessContext(DbContextOptions<KeylessContext> options) : DbContext(options), ITenantContext
    {
        public Guid ChurchId { get; set; }
        public DbSet<Keyless> Keylesses => Set<Keyless>();
    }

    private static TestContext Create(string database, Guid churchId) => new(
        new DbContextOptionsBuilder<TestContext>()
            .UseInMemoryDatabase(database)
            .UseMultiTenancy()
            .Options)
    { ChurchId = churchId };

    // ITenanted is the opt-in: shared entities (Church, LeaderToken, DataProtectionKeys) must stay unfiltered.
    [Test]
    public async Task SaveChangesAsync_UntenantedEntityAcrossChurches_IsNotFiltered()
    {
        string database = nameof(SaveChangesAsync_UntenantedEntityAcrossChurches_IsNotFiltered);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        using (TestContext asA = Create(database, a))
        {
            asA.Untenanteds.Add(new Untenanted());
            await asA.SaveChangesAsync();
        }

        using TestContext asB = Create(database, b);
        await Assert.That(await asB.Untenanteds.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Include_ParentOwnedByOtherChurch_ReturnsNullParent()
    {
        string database = nameof(Include_ParentOwnedByOtherChurch_ReturnsNullParent);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), parentId = Guid.NewGuid();
        using (TestContext asB = Create(database, b))
        {
            asB.Parents.Add(new Parent { Id = parentId, ChurchId = b });
            await asB.SaveChangesAsync();
        }

        using (TestContext asA = Create(database, a))
        {
            asA.Children.Add(new Child { Id = Guid.NewGuid(), ChurchId = a, ParentId = parentId });
            await asA.SaveChangesAsync();
        }

        using TestContext reader = Create(database, a);
        Child child = await reader.Children.Include(c => c.Parent).SingleAsync();
        await Assert.That(child.Parent).IsNull();
    }
    [Test]
    public async Task FilterIsolatesRowsByCurrentChurch()
    {
        string database = nameof(FilterIsolatesRowsByCurrentChurch);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        using (TestContext seed = Create(database, a))
        {
            seed.Parents.Add(new Parent { Id = Guid.NewGuid(), ChurchId = a });
            seed.Parents.Add(new Parent { Id = Guid.NewGuid(), ChurchId = b });
            await seed.SaveChangesAsync();
        }

        using TestContext asA = Create(database, a);
        using TestContext asB = Create(database, b);
        await Assert.That(await asA.Parents.CountAsync()).IsEqualTo(1);
        Parent visibleToA = await asA.Parents.SingleAsync();
        Parent visibleToB = await asB.Parents.SingleAsync();
        await Assert.That(visibleToA.ChurchId).IsEqualTo(a);
        await Assert.That(visibleToB.ChurchId).IsEqualTo(b);
        await Assert.That(await asA.Parents.IgnoreQueryFilters().CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task Model_TenantedEntityThatCannotBeProtected_ThrowsInvalidOperationException()
    {
        using KeylessContext db = new(
            new DbContextOptionsBuilder<KeylessContext>()
                .UseInMemoryDatabase(nameof(Model_TenantedEntityThatCannotBeProtected_ThrowsInvalidOperationException))
                .UseMultiTenancy()
                .Options);

        await Assert.That(() => db.Model).Throws<InvalidOperationException>().WithMessageContaining("Tenancy cannot be enforced for").And.WithMessageContaining("Keyless");
    }
}
