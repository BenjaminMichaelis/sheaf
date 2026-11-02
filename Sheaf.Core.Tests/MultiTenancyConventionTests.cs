using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

public class MultiTenancyConventionTests
{
    private static readonly string[] PkNames = ["ChurchId", "Id"];
    private static readonly string[] FkNames = ["ChurchId", "ParentId"];
    private static readonly string[] FilterNames = ["TenancyFilter"];

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
        public Guid ParentId { get; set; }
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

    private static IEntityType EntityOf(DbContext db, Type type) =>
        db.Model.FindEntityType(type) ?? throw new InvalidOperationException($"{type.Name} is not in the model.");

    private static IKey PrimaryKeyOf(DbContext db, Type type) =>
        EntityOf(db, type).FindPrimaryKey() ?? throw new InvalidOperationException($"{type.Name} has no primary key.");

    [Test]
    public async Task PrimaryKeyIsComposite_ChurchIdThenId()
    {
        using TestContext db = Create(nameof(PrimaryKeyIsComposite_ChurchIdThenId), Guid.NewGuid());
        foreach (Type type in new[] { typeof(Parent), typeof(Child) })
        {
            IKey key = PrimaryKeyOf(db, type);
            await Assert.That(key.Properties.Select(p => p.Name).ToArray())
                .IsEquivalentTo(PkNames);
            await Assert.That(key.Properties[0].Name).IsEqualTo("ChurchId");
        }

        await Assert.That(PrimaryKeyOf(db, typeof(Untenanted)).Properties.Count).IsEqualTo(1);
    }

    [Test]
    public async Task NamedTenancyFilterIsPresentOnTenantedEntitiesOnly()
    {
        using TestContext db = Create(nameof(NamedTenancyFilterIsPresentOnTenantedEntitiesOnly), Guid.NewGuid());
        foreach (Type type in new[] { typeof(Parent), typeof(Child) })
        {
            IEntityType entityType = EntityOf(db, type);
            await Assert.That(entityType.GetDeclaredQueryFilters().Select(f => f.Key).OfType<string>().ToArray())
                .IsEquivalentTo(FilterNames);
        }

        await Assert.That(EntityOf(db, typeof(Untenanted)).GetDeclaredQueryFilters().Count).IsEqualTo(0);
    }

    [Test]
    public async Task ForeignKeyToTenantedPrincipalHasArityTwoOnChurchIdAndFk()
    {
        using TestContext db = Create(nameof(ForeignKeyToTenantedPrincipalHasArityTwoOnChurchIdAndFk), Guid.NewGuid());
        IForeignKey fk = EntityOf(db, typeof(Child)).GetForeignKeys().Single();

        await Assert.That(fk.Properties.Select(p => p.Name).ToArray()).IsEquivalentTo(FkNames);
        await Assert.That(fk.Properties[0].Name).IsEqualTo("ChurchId");
        await Assert.That(fk.PrincipalKey.Properties.Select(p => p.Name).ToArray())
            .IsEquivalentTo(PkNames);
        await Assert.That(fk.PrincipalEntityType.ClrType).IsEqualTo(typeof(Parent));
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
    public async Task TenantedEntityThatCannotBeProtectedFailsModelBuilding()
    {
        using KeylessContext db = new(
            new DbContextOptionsBuilder<KeylessContext>()
                .UseInMemoryDatabase(nameof(TenantedEntityThatCannotBeProtectedFailsModelBuilding))
                .UseMultiTenancy()
                .Options);

        await Assert.That(() => db.Model).Throws<InvalidOperationException>().WithMessageContaining("Tenancy cannot be enforced for").And.WithMessageContaining("Keyless");
    }
}
