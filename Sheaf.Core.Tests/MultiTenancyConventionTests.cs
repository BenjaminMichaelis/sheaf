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

    private static TestContext Create(string database, Guid churchId) => new(
        new DbContextOptionsBuilder<TestContext>()
            .UseInMemoryDatabase(database)
            .UseMultiTenancy()
            .Options)
    { ChurchId = churchId };

    [Test]
    public async Task PrimaryKeyIsComposite_ChurchIdThenId()
    {
        using TestContext db = Create(nameof(PrimaryKeyIsComposite_ChurchIdThenId), Guid.NewGuid());
        foreach (Type type in new[] { typeof(Parent), typeof(Child) })
        {
            IKey key = db.Model.FindEntityType(type)!.FindPrimaryKey()!;
            await Assert.That(key.Properties.Select(p => p.Name).ToArray())
                .IsEquivalentTo(PkNames);
            await Assert.That(key.Properties[0].Name).IsEqualTo("ChurchId");
        }

        await Assert.That(db.Model.FindEntityType(typeof(Untenanted))!.FindPrimaryKey()!.Properties.Count).IsEqualTo(1);
    }

    [Test]
    public async Task NamedTenancyFilterIsPresentOnTenantedEntitiesOnly()
    {
        using TestContext db = Create(nameof(NamedTenancyFilterIsPresentOnTenantedEntitiesOnly), Guid.NewGuid());
        foreach (Type type in new[] { typeof(Parent), typeof(Child) })
        {
            IEntityType entityType = db.Model.FindEntityType(type)!;
            await Assert.That(entityType.GetDeclaredQueryFilters().Select(f => f.Key!).ToArray())
                .IsEquivalentTo(FilterNames);
        }

        await Assert.That(db.Model.FindEntityType(typeof(Untenanted))!.GetDeclaredQueryFilters().Count).IsEqualTo(0);
    }

    [Test]
    public async Task ForeignKeyToTenantedPrincipalHasArityTwoOnChurchIdAndFk()
    {
        using TestContext db = Create(nameof(ForeignKeyToTenantedPrincipalHasArityTwoOnChurchIdAndFk), Guid.NewGuid());
        IForeignKey fk = db.Model.FindEntityType(typeof(Child))!.GetForeignKeys().Single();

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
        await Assert.That(await asA.Parents.SingleAsync()).Satisfies(p => p!.ChurchId, c => c.IsEqualTo(a));
        await Assert.That(await asB.Parents.SingleAsync()).Satisfies(p => p!.ChurchId, c => c.IsEqualTo(b));
        await Assert.That(await asA.Parents.IgnoreQueryFilters().CountAsync()).IsEqualTo(2);
    }
}
