using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Sheaf.Data;
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

    // ITenanted is the opt-in: shared entities such as Church and Identity users must stay unfiltered.
    [Test]
    public async Task Query_UntenantedEntityAddedByOtherChurch_IsVisible()
    {
        string database = nameof(Query_UntenantedEntityAddedByOtherChurch_IsVisible);
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
        using (TestContext seedA = Create(database, a))
        {
            seedA.Parents.Add(new Parent { Id = Guid.NewGuid(), ChurchId = a });
            await seedA.SaveChangesAsync();
        }

        using (TestContext seedB = Create(database, b))
        {
            seedB.Parents.Add(new Parent { Id = Guid.NewGuid(), ChurchId = b });
            await seedB.SaveChangesAsync();
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

    [Test]
    public async Task Model_NonTenantedDependentOfTenantedPrincipal_ThrowsInvalidOperationException()
    {
        using GlobalReferenceContext context = new(CreateModelOptions<GlobalReferenceContext>(
            nameof(Model_NonTenantedDependentOfTenantedPrincipal_ThrowsInvalidOperationException)));

        await Assert.That(() => context.Model).Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(SharedPrincipalReference))
            .And.WithMessageContaining(nameof(MixedPrincipal));
    }

    [Test]
    public async Task Model_NonTenantedDependentViaAlternateKey_ThrowsInvalidOperationException()
    {
        using AlternateGlobalReferenceContext context = new(CreateModelOptions<AlternateGlobalReferenceContext>(
            nameof(Model_NonTenantedDependentViaAlternateKey_ThrowsInvalidOperationException)));

        await Assert.That(() => context.Model).Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(SharedAlternateKeyReference))
            .And.WithMessageContaining(nameof(MixedPrincipal));
    }

    [Test]
    public async Task Model_ImplicitManyToManyBetweenTenantedEntities_ThrowsInvalidOperationException()
    {
        using ImplicitJoinContext context = new(CreateModelOptions<ImplicitJoinContext>(
            nameof(Model_ImplicitManyToManyBetweenTenantedEntities_ThrowsInvalidOperationException)));

        await Assert.That(() => context.Model).Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(ManyToManyLeft))
            .And.WithMessageContaining(nameof(ManyToManyRight))
            .And.WithMessageContaining("implicit many-to-many");
    }

    [Test]
    public async Task Model_TenantedDependentOfApplicationUser_BuildsWithoutChurchIdInForeignKey()
    {
        using TenancyArchitectureFixture fixture = new();
        IEntityType dependent = FindEntity(fixture.ProbeContext.Model, typeof(TenantPrincipalProbe));
        IForeignKey foreignKey = dependent.GetDeclaredForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(ApplicationUser));

        await Assert.That(string.Join(",", foreignKey.Properties.Select(property => property.Name)))
            .IsEqualTo(nameof(TenantPrincipalProbe.UserId));
        await Assert.That(foreignKey.PrincipalKey.IsPrimaryKey()).IsTrue();
        await Assert.That(dependent.FindDeclaredQueryFilter(MultiTenancyConvention.FilterName)).IsNotNull();
    }

    [Test]
    public async Task Model_TenantedDependentOfChurch_Builds()
    {
        using TenancyArchitectureFixture fixture = new();
        IEntityType dependent = FindEntity(fixture.ProbeContext.Model, typeof(TenantRecord));
        IForeignKey foreignKey = dependent.GetDeclaredForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Church));
        IProperty churchId = dependent.FindProperty(nameof(ITenanted.ChurchId))
            ?? throw new InvalidOperationException("The tenanted probe is missing ChurchId.");

        await Assert.That(foreignKey.Properties.Count).IsEqualTo(1);
        await Assert.That(foreignKey.Properties[0] == churchId).IsTrue();
        await Assert.That(foreignKey.PrincipalKey.IsPrimaryKey()).IsTrue();
        await Assert.That(string.Join(",", foreignKey.PrincipalKey.Properties.Select(property => property.Name)))
            .IsEqualTo(nameof(Church.Id));
        await Assert.That(foreignKey.IsRequired).IsTrue();
        await Assert.That(foreignKey.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
    }

    [Test]
    public async Task Model_TenantedEntityWithOwnedJsonType_Builds()
    {
        using TenancyArchitectureFixture fixture = new();
        IEntityType owner = FindEntity(fixture.ProbeContext.Model, typeof(TenantPrincipalProbe));
        INavigation fields = owner.FindNavigation(nameof(TenantPrincipalProbe.Fields))
            ?? throw new InvalidOperationException("The tenanted probe is missing its JSON navigation.");
        IEntityType owned = fields.TargetEntityType;
        IForeignKey ownership = owned.FindOwnership()
            ?? throw new InvalidOperationException("The JSON probe is missing its ownership relationship.");

        await Assert.That(owned.ClrType).IsEqualTo(typeof(TenantJsonProbe));
        await Assert.That(owned.IsOwned()).IsTrue();
        await Assert.That(owned.IsMappedToJson()).IsTrue();
        await Assert.That(ownership.PrincipalEntityType.ClrType).IsEqualTo(typeof(TenantPrincipalProbe));
        await Assert.That(ownership.PrincipalEntityType.FindDeclaredQueryFilter(MultiTenancyConvention.FilterName)).IsNotNull();
    }

    [Test]
    public async Task Model_ExplicitTenantedJoinEntity_IncludesChurchIdInBothForeignKeys()
    {
        using TenancyArchitectureFixture fixture = new();
        IEntityType join = FindEntity(fixture.ProbeContext.Model, typeof(TenantJoinProbe));
        List<IForeignKey> foreignKeys = join.GetDeclaredForeignKeys()
            .Where(foreignKey => typeof(ITenanted).IsAssignableFrom(foreignKey.PrincipalEntityType.ClrType))
            .ToList();

        await Assert.That(foreignKeys.Count).IsEqualTo(2);
        foreach (IForeignKey foreignKey in foreignKeys)
        {
            string idProperty = foreignKey.PrincipalEntityType.ClrType == typeof(TenantRecord)
                ? nameof(TenantJoinProbe.RecordId)
                : nameof(TenantJoinProbe.PrincipalId);
            await AssertTenantedForeignKey(foreignKey, join, idProperty);
            await Assert.That(foreignKey.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
        }
    }

    [Test]
    public async Task Model_SelfReferenceOnTenantedEntity_IncludesChurchIdInForeignKey()
    {
        using SelfReferenceContext context = new(CreateModelOptions<SelfReferenceContext>(
            nameof(Model_SelfReferenceOnTenantedEntity_IncludesChurchIdInForeignKey)));
        IEntityType dependent = FindEntity(context.Model, typeof(SelfReferenceProbe));
        IForeignKey foreignKey = dependent.GetDeclaredForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType == dependent);

        await AssertTenantedForeignKey(foreignKey, dependent, nameof(SelfReferenceProbe.GuardianId));
        await Assert.That(foreignKey.IsRequired).IsFalse();
        await Assert.That(foreignKey.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
    }

    [Test]
    public async Task Model_ExplicitRestrictOnTenantedForeignKey_KeepsChurchIdAndRestrict()
    {
        using TenancyArchitectureFixture fixture = new();
        IEntityType dependent = FindEntity(fixture.ProbeContext.Model, typeof(TenantJoinProbe));
        IForeignKey foreignKey = dependent.GetDeclaredForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(TenantRecord));

        await AssertTenantedForeignKey(foreignKey, dependent, nameof(TenantJoinProbe.RecordId));
        await Assert.That(foreignKey.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
    }

    private static DbContextOptions<TContext> CreateModelOptions<TContext>(string database)
        where TContext : DbContext, ITenantContext =>
        new DbContextOptionsBuilder<TContext>().UseInMemoryDatabase(database).UseMultiTenancy().Options;

    private static IEntityType FindEntity(IModel model, Type type) =>
        model.FindEntityType(type) ?? throw new InvalidOperationException($"The probe model is missing {type.Name}.");

    private static async Task AssertTenantedForeignKey(IForeignKey foreignKey, IEntityType dependent, string idProperty)
    {
        IProperty churchId = dependent.FindProperty(nameof(ITenanted.ChurchId))
            ?? throw new InvalidOperationException($"{dependent.DisplayName()} is missing ChurchId.");

        await Assert.That(foreignKey.PrincipalKey.IsPrimaryKey()).IsTrue();
        await Assert.That(string.Join(",", foreignKey.PrincipalKey.Properties.Select(property => property.Name)))
            .IsEqualTo($"{nameof(ITenanted.ChurchId)},Id");
        await Assert.That(string.Join(",", foreignKey.Properties.Select(property => property.Name)))
            .IsEqualTo($"{nameof(ITenanted.ChurchId)},{idProperty}");
        await Assert.That(foreignKey.Properties[0] == churchId).IsTrue();
    }

    private sealed class MixedPrincipal : ITenanted
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }
        public string Code { get; set; } = string.Empty;
    }

    private sealed class SharedPrincipalReference
    {
        public Guid Id { get; set; }
        public Guid PrincipalId { get; set; }
        public MixedPrincipal? Principal { get; set; }
    }

    private sealed class SharedAlternateKeyReference
    {
        public Guid Id { get; set; }
        public string PrincipalCode { get; set; } = string.Empty;
        public MixedPrincipal? Principal { get; set; }
    }

    private sealed class GlobalReferenceContext(DbContextOptions<GlobalReferenceContext> options) : DbContext(options), ITenantContext
    {
        public Guid ChurchId => Guid.Empty;

        protected override void OnModelCreating(ModelBuilder builder) =>
            builder.Entity<SharedPrincipalReference>().HasOne(reference => reference.Principal).WithMany()
                .HasForeignKey(reference => reference.PrincipalId);
    }

    private sealed class AlternateGlobalReferenceContext(DbContextOptions<AlternateGlobalReferenceContext> options) : DbContext(options), ITenantContext
    {
        public Guid ChurchId => Guid.Empty;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<MixedPrincipal>().HasAlternateKey(principal => principal.Code);
            builder.Entity<SharedAlternateKeyReference>().HasOne(reference => reference.Principal).WithMany()
                .HasForeignKey(reference => reference.PrincipalCode)
                .HasPrincipalKey(principal => principal.Code);
        }
    }

    private sealed class ManyToManyLeft : ITenanted
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }
        public List<ManyToManyRight> Rights { get; set; } = [];
    }

    private sealed class ManyToManyRight : ITenanted
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }
        public List<ManyToManyLeft> Lefts { get; set; } = [];
    }

    private sealed class ImplicitJoinContext(DbContextOptions<ImplicitJoinContext> options) : DbContext(options), ITenantContext
    {
        public Guid ChurchId => Guid.Empty;

        protected override void OnModelCreating(ModelBuilder builder) =>
            builder.Entity<ManyToManyLeft>().HasMany(left => left.Rights).WithMany(right => right.Lefts);
    }

    private sealed class SelfReferenceProbe : ITenanted
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }
        public Guid? GuardianId { get; set; }
        public SelfReferenceProbe? Guardian { get; set; }
    }

    private sealed class SelfReferenceContext(DbContextOptions<SelfReferenceContext> options) : DbContext(options), ITenantContext
    {
        public Guid ChurchId => Guid.Empty;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<SelfReferenceProbe>().HasOne(probe => probe.Guardian).WithMany()
                .HasForeignKey(probe => probe.GuardianId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SelfReferenceProbe>().HasOne<Church>().WithMany()
                .HasForeignKey(probe => probe.ChurchId).OnDelete(DeleteBehavior.Restrict).IsRequired();
        }
    }
}
