using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Sheaf.Data;
using Sheaf.Data.Tenancy;

using TUnit.Assertions.Enums;

namespace Sheaf.Core.Tests;

public class TenancyArchitectureTests
{
    private static readonly HashSet<Type> SharedEntityTypes =
    [
        typeof(Church),
        typeof(ApplicationUser),
        typeof(IdentityRole),
        typeof(IdentityUserClaim<string>),
        typeof(IdentityUserLogin<string>),
        typeof(IdentityUserRole<string>),
        typeof(IdentityUserToken<string>),
        typeof(IdentityRoleClaim<string>),
        typeof(IdentityUserPasskey<string>)
    ];

    [Test]
    public async Task GetEntityTypes_ProductionModelFromAddDatabase_ConformToTenancyArchitecture()
    {
        using TenancyArchitectureFixture fixture = new();

        List<string> violations = GetArchitectureViolations(fixture.RuntimeContext.Model);

        await Assert.That(violations).IsEmpty().Because(string.Join(Environment.NewLine, violations));
    }

    [Test]
    public async Task GetEntityTypes_ProductionOptionsProbeModel_ConformToTenancyArchitecture()
    {
        using TenancyArchitectureFixture fixture = new();
        IModel model = fixture.ProbeContext.Model;

        await Assert.That(model.GetEntityTypes().Any(IsTenanted)).IsTrue();
        List<string> violations = GetArchitectureViolations(model);

        await Assert.That(violations).IsEmpty().Because(string.Join(Environment.NewLine, violations));
    }

    [Test]
    public async Task GetForeignKeys_TenantedDependents_UseRestrictOrNoAction()
    {
        using TenancyArchitectureFixture fixture = new();
        IModel[] models = [fixture.RuntimeContext.Model, fixture.ProbeContext.Model];

        await Assert.That(models.SelectMany(model => model.GetEntityTypes())
            .Where(IsTenanted)
            .SelectMany(entityType => entityType.GetDeclaredForeignKeys())
            .Where(foreignKey => !foreignKey.IsOwnership)).IsNotEmpty();
        List<string> violations = models.SelectMany(GetDeleteBehaviorViolations).ToList();

        await Assert.That(violations).IsEmpty().Because(string.Join(Environment.NewLine, violations));
    }

    [Test]
    public async Task GetForeignKeys_TenantedRoots_IncludeRequiredChurchForeignKey()
    {
        using TenancyArchitectureFixture fixture = new();
        IModel[] models = [fixture.RuntimeContext.Model, fixture.ProbeContext.Model];

        await Assert.That(fixture.ProbeContext.Model.GetEntityTypes().Any(IsTenanted)).IsTrue();
        List<string> violations = models.SelectMany(GetChurchForeignKeyViolations).ToList();

        await Assert.That(violations).IsEmpty().Because(string.Join(Environment.NewLine, violations));
    }

    [Test]
    public async Task SharedEntityAllowlist_EveryEntry_ExistsInProductionModel()
    {
        using TenancyArchitectureFixture fixture = new();
        HashSet<Type> productionTypes = fixture.RuntimeContext.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType).ToHashSet();
        List<string> staleEntries = SharedEntityTypes
            .Where(type => !productionTypes.Contains(type))
            .Select(type => $"{type.FullName} is allowlisted but absent from the production model.")
            .ToList();

        await Assert.That(staleEntries).IsEmpty().Because(string.Join(Environment.NewLine, staleEntries));
    }

    [Test]
    public async Task GetEntityTypes_ProductionModel_ContainsTenantedEntities()
    {
        using TenancyArchitectureFixture fixture = new();
        IEntityType membership = fixture.RuntimeContext.Model.FindEntityType(typeof(ChurchMembership))
            ?? throw new InvalidOperationException("ChurchMembership is missing from the production model.");
        Type[] tenantedTypes = fixture.RuntimeContext.Model.GetEntityTypes()
            .Where(IsTenanted)
            .Select(entityType => entityType.ClrType)
            .ToArray();
        IKey primaryKeyMetadata = membership.FindPrimaryKey()
            ?? throw new InvalidOperationException("ChurchMembership has no primary key.");
        string[] primaryKey = primaryKeyMetadata.Properties.Select(property => property.Name).ToArray();
        IIndex membershipIndex = membership.GetIndexes()
            .Single(index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ChurchMembership.ChurchId), nameof(ChurchMembership.UserId)]));

        await Assert.That(tenantedTypes).IsNotEmpty();
        await Assert.That(tenantedTypes).Contains(typeof(ChurchMembership));
        await Assert.That(primaryKey).IsEquivalentTo(
            [nameof(ChurchMembership.ChurchId), nameof(ChurchMembership.Id)],
            CollectionOrdering.Matching);
        await Assert.That(membershipIndex.IsUnique).IsTrue();
    }

    [Test]
    public async Task GetForeignKeys_ProbeWithoutRequiredChurchForeignKey_ReportsViolation()
    {
        using TenancyArchitectureFixture fixture = new();
        using MissingChurchForeignKeyContext context = new(fixture.Options);

        await AssertViolation(GetChurchForeignKeyViolations(context.Model), nameof(TenantRecord), "required Church foreign key");
    }

    [Test]
    public async Task GetForeignKeys_ProbeWithSeparateChurchIdShadowProperty_ReportsViolation()
    {
        using TenancyArchitectureFixture fixture = new();
        using SeparateChurchIdContext context = new(fixture.Options);

        await AssertViolation(GetChurchForeignKeyViolations(context.Model), nameof(TenantRecord), "required Church foreign key");
    }

    [Test]
    public async Task GetEntityTypes_DerivedTenantedEntityWithUnscopedUniqueIndex_ReportsViolation()
    {
        using TenancyArchitectureFixture fixture = new();
        using UnscopedDerivedIndexContext context = new(fixture.Options);

        await AssertViolation(GetArchitectureViolations(context.Model), nameof(DerivedTenantRecord), "unique index without ChurchId");
    }

    [Test]
    public async Task GetKeys_TenantedAlternateKeyWithoutChurchId_ReportsViolation()
    {
        using TenancyArchitectureFixture fixture = new();
        using UnscopedAlternateKeyContext context = new(fixture.Options);

        await AssertViolation(GetArchitectureViolations(context.Model), nameof(TenantPrincipalProbe), "alternate key without ChurchId");
    }

    [Test]
    public async Task GetForeignKeys_TenantedDependentReferencingAlternateKey_ReportsViolation()
    {
        using TenancyArchitectureFixture fixture = new();
        using AlternatePrincipalKeyContext context = new(fixture.Options);

        await AssertViolation(GetArchitectureViolations(context.Model), nameof(DerivedTenantRecord), "does not reference its primary key");
    }

    [Test]
    public async Task GetForeignKeys_TenantedDependentWithMisalignedChurchId_ReportsViolation()
    {
        using TenancyArchitectureFixture fixture = new();
        using MisalignedChurchIdContext context = new(fixture.Options);

        await AssertViolation(GetArchitectureViolations(context.Model), nameof(DerivedTenantRecord), "does not align ChurchId");
    }

    [Test]
    public async Task GetForeignKeys_TenantedDependentOfApplicationUserWithClientCascade_ReportsViolation()
    {
        using TenancyArchitectureFixture fixture = new();
        using ClientCascadeContext context = new(fixture.Options);

        await AssertViolation(GetDeleteBehaviorViolations(context.Model), nameof(TenantPrincipalProbe), nameof(DeleteBehavior.ClientCascade));
    }

    [Test]
    public async Task GetForeignKeys_OptionalTenantedSelfReferenceWithClientSetNull_ReportsViolation()
    {
        using TenancyArchitectureFixture fixture = new();
        using ClientSetNullContext context = new(fixture.Options);

        await AssertViolation(GetDeleteBehaviorViolations(context.Model), nameof(TenantPrincipalProbe), nameof(DeleteBehavior.ClientSetNull));
    }

    [Test]
    public async Task ExecuteUpdateDelete_ForCurrentTenant_OnlyAffectsCurrentTenantRecords()
    {
        using SqliteFixture sqlite = new();
        Church churchARecord = new() { Slug = "architecture-a", Name = "Church A" };
        Church churchBRecord = new() { Slug = "architecture-b", Name = "Church B" };
        Guid churchA = churchARecord.Id;
        Guid churchB = churchBRecord.Id;
        Guid personAId = Guid.NewGuid();
        Guid personBId = Guid.NewGuid();

        await using (TenancyTestDbContext churchSeed = sqlite.CreateContext(null))
        {
            churchSeed.Churches.AddRange(churchARecord, churchBRecord);
            await churchSeed.SaveChangesAsync();
        }

        await using (TenancyTestDbContext seedA = sqlite.CreateContext(churchA))
        {
            seedA.People.Add(new Person
            {
                Id = personAId,
                ChurchId = churchA,
                Name = "Church A person",
                Email = "church-a@example.com",
            });
            await seedA.SaveChangesAsync();
        }

        await using (TenancyTestDbContext seedB = sqlite.CreateContext(churchB))
        {
            seedB.People.Add(new Person
            {
                Id = personBId,
                ChurchId = churchB,
                Name = "Church B person",
                Email = "church-b@example.com",
            });
            await seedB.SaveChangesAsync();
        }

        await using TenancyTestDbContext asChurchA = sqlite.CreateContext(churchA);
        int updatedRows = await asChurchA.People.ExecuteUpdateAsync(
            people => people.SetProperty(person => person.Name, "Updated by Church A"));

        await Assert.That(updatedRows).IsEqualTo(1);
        await Assert.That(await asChurchA.People.IgnoreQueryFilters()
            .Where(person => person.Id == personBId)
            .Select(person => person.Name)
            .SingleAsync()).IsEqualTo("Church B person");
        await Assert.That(await asChurchA.People
            .Where(person => person.Id == personAId)
            .Select(person => person.Name)
            .SingleAsync()).IsEqualTo("Updated by Church A");

        int deletedRows = await asChurchA.People.ExecuteDeleteAsync();

        await Assert.That(deletedRows).IsEqualTo(1);
        await Assert.That(await asChurchA.People.IgnoreQueryFilters()
            .AnyAsync(person => person.Id == personAId)).IsFalse();
        await Assert.That(await asChurchA.People.IgnoreQueryFilters()
            .AnyAsync(person => person.Id == personBId)).IsTrue();
    }

    private static List<string> GetArchitectureViolations(IModel model)
    {
        List<string> violations = [];
        HashSet<IEntityType> checkedRoots = [];

        foreach (IEntityType entityType in model.GetEntityTypes())
        {
            IEntityType root = GetClassificationRoot(entityType);
            bool isTenanted = IsTenanted(entityType);
            if (!isTenanted && !SharedEntityTypes.Contains(root.ClrType))
            {
                violations.Add($"{entityType.DisplayName()} is neither ITenanted nor an allowed shared entity.");
            }

            IProperty? churchId = isTenanted ? GetChurchIdProperty(entityType) : null;
            if (isTenanted)
            {
                if (checkedRoots.Add(root)
                    && !root.GetDeclaredQueryFilters().Any(filter => filter.Key == MultiTenancyConvention.FilterName))
                {
                    violations.Add($"{root.DisplayName()} does not declare the {MultiTenancyConvention.FilterName} query filter.");
                }

                IKey? primaryKey = entityType.FindPrimaryKey();
                if (churchId is null || primaryKey is null
                    || primaryKey.Properties.Count == 0 || primaryKey.Properties[0] != churchId)
                {
                    violations.Add($"{entityType.DisplayName()} does not have its ChurchId as the first primary-key property.");
                }

                foreach (IKey key in entityType.GetKeys().Where(key => !key.IsPrimaryKey()))
                {
                    if (churchId is null || !key.Properties.Contains(churchId))
                    {
                        violations.Add($"{entityType.DisplayName()} has an alternate key without ChurchId.");
                    }
                }

                foreach (IIndex index in entityType.GetIndexes().Where(index => index.IsUnique))
                {
                    if (churchId is null || !index.Properties.Contains(churchId))
                    {
                        violations.Add($"{entityType.DisplayName()} has a unique index without ChurchId.");
                    }
                }
            }

            foreach (IForeignKey foreignKey in entityType.GetDeclaredForeignKeys()
                .Where(foreignKey => !foreignKey.IsOwnership && IsTenanted(foreignKey.PrincipalEntityType)))
            {
                IEntityType principal = foreignKey.PrincipalEntityType;
                if (!isTenanted)
                {
                    violations.Add($"{entityType.DisplayName()} is non-tenanted but references tenanted {principal.DisplayName()}.");
                }

                if (!foreignKey.PrincipalKey.IsPrimaryKey())
                {
                    violations.Add($"{entityType.DisplayName()}'s foreign key to {principal.DisplayName()} does not reference its primary key.");
                }

                IProperty? principalChurchId = GetChurchIdProperty(principal);
                bool aligned = false;
                for (int i = 0; i < foreignKey.PrincipalKey.Properties.Count; i++)
                {
                    if (principalChurchId is not null && foreignKey.PrincipalKey.Properties[i] == principalChurchId)
                    {
                        aligned = churchId is not null && i < foreignKey.Properties.Count
                            && foreignKey.Properties[i] == churchId;
                        break;
                    }
                }

                if (!aligned)
                {
                    violations.Add($"{entityType.DisplayName()}'s foreign key to {principal.DisplayName()} does not align ChurchId with the principal's ChurchId.");
                }
            }
        }

        violations.AddRange(GetDeleteBehaviorViolations(model));
        violations.AddRange(GetChurchForeignKeyViolations(model));
        return violations;
    }

    private static List<string> GetDeleteBehaviorViolations(IModel model) =>
        model.GetEntityTypes()
            .Where(IsTenanted)
            .SelectMany(entityType => entityType.GetDeclaredForeignKeys())
            .Where(foreignKey => !foreignKey.IsOwnership
                && foreignKey.DeleteBehavior is not (DeleteBehavior.Restrict or DeleteBehavior.NoAction))
            .Select(foreignKey =>
                $"{foreignKey.DeclaringEntityType.DisplayName()}'s foreign key to {foreignKey.PrincipalEntityType.DisplayName()} uses {foreignKey.DeleteBehavior}, not Restrict or NoAction.")
            .ToList();

    private static List<string> GetChurchForeignKeyViolations(IModel model)
    {
        List<string> violations = [];
        foreach (IEntityType root in model.GetEntityTypes().Select(GetClassificationRoot).Where(IsTenanted).Distinct())
        {
            IProperty? churchId = root.FindProperty(nameof(ITenanted.ChurchId));
            bool hasChurchForeignKey = root.GetForeignKeys().Any(foreignKey =>
                !foreignKey.IsOwnership
                && foreignKey.PrincipalEntityType.GetRootType().ClrType == typeof(Church)
                && foreignKey.IsRequired
                && foreignKey.PrincipalKey.IsPrimaryKey()
                && foreignKey.Properties.Count == 1
                && churchId is not null && foreignKey.Properties[0] == churchId
                && foreignKey.PrincipalKey.Properties.Count == 1
                && foreignKey.PrincipalKey.Properties[0].Name == nameof(Church.Id)
                && foreignKey.DeleteBehavior is DeleteBehavior.Restrict or DeleteBehavior.NoAction);
            if (!hasChurchForeignKey)
            {
                violations.Add($"{root.DisplayName()} is missing a required Church foreign key from (ChurchId) to Church (Id) with Restrict or NoAction.");
            }
        }

        return violations;
    }

    private static bool IsTenanted(IEntityType entityType) =>
        typeof(ITenanted).IsAssignableFrom(GetClassificationRoot(entityType).ClrType);

    private static IEntityType GetClassificationRoot(IEntityType entityType)
    {
        IEntityType root = entityType.GetRootType();
        while (root.FindOwnership() is { } ownership)
        {
            root = ownership.PrincipalEntityType.GetRootType();
        }

        return root;
    }

    private static IProperty? GetChurchIdProperty(IEntityType entityType)
    {
        if (entityType.FindOwnership() is not { } ownership)
        {
            return entityType.FindProperty(nameof(ITenanted.ChurchId));
        }

        // Owned keys use shadow properties; follow the owner's tenant property through the ownership FK.
        IProperty? ownerChurchId = GetChurchIdProperty(ownership.PrincipalEntityType);
        for (int i = 0; i < ownership.PrincipalKey.Properties.Count; i++)
        {
            if (ownerChurchId is not null && ownership.PrincipalKey.Properties[i] == ownerChurchId)
            {
                return ownership.Properties[i];
            }
        }

        return null;
    }

    private static async Task AssertViolation(IEnumerable<string> violations, string entityName, string reason)
    {
        List<string> actual = violations.ToList();
        await Assert.That(actual.Any(violation =>
            violation.Contains(entityName, StringComparison.Ordinal)
            && violation.Contains(reason, StringComparison.Ordinal))).IsTrue()
            .Because(string.Join(Environment.NewLine, actual));
    }

    private sealed class MissingChurchForeignKeyContext(DbContextOptions<ApplicationDbContext> options) : TenantTestDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            IMutableEntityType entity = builder.Entity<TenantRecord>().Metadata;
            IMutableForeignKey churchForeignKey = entity.GetForeignKeys()
                .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Church));
            entity.RemoveForeignKey(churchForeignKey);
        }
    }

    private sealed class SeparateChurchIdContext(DbContextOptions<ApplicationDbContext> options) : TenantTestDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            IMutableEntityType entity = builder.Entity<TenantRecord>().Metadata;
            IMutableForeignKey churchForeignKey = entity.GetForeignKeys()
                .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Church));
            entity.RemoveForeignKey(churchForeignKey);
            builder.Entity<TenantRecord>().Property<Guid>("SeparateChurchId");
            builder.Entity<TenantRecord>().HasOne<Church>().WithMany()
                .HasForeignKey("SeparateChurchId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        }
    }

    private sealed class UnscopedDerivedIndexContext(DbContextOptions<ApplicationDbContext> options)
        : TenancyArchitectureDbContext(options, new TenantContext())
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<DerivedTenantRecord>().HasIndex(record => record.Detail).IsUnique();
        }
    }

    private sealed class UnscopedAlternateKeyContext(DbContextOptions<ApplicationDbContext> options)
        : TenancyArchitectureDbContext(options, new TenantContext())
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<TenantPrincipalProbe>().HasAlternateKey(principal => principal.UserId);
        }
    }

    private sealed class AlternatePrincipalKeyContext(DbContextOptions<ApplicationDbContext> options)
        : TenancyArchitectureDbContext(options, new TenantContext())
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<DerivedTenantRecord>().HasOne<TenantPrincipalProbe>().WithMany()
                .HasPrincipalKey(nameof(ITenanted.ChurchId), nameof(TenantPrincipalProbe.Code))
                .HasForeignKey(nameof(ITenanted.ChurchId), "PrincipalCode")
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class MisalignedChurchIdContext(DbContextOptions<ApplicationDbContext> options)
        : TenancyArchitectureDbContext(options, new TenantContext())
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<DerivedTenantRecord>().HasOne<TenantPrincipalProbe>().WithMany()
                .HasPrincipalKey(nameof(ITenanted.ChurchId), nameof(TenantPrincipalProbe.Id))
                .HasForeignKey("ReferencedChurchId", nameof(DerivedTenantRecord.PrincipalId))
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class ClientCascadeContext(DbContextOptions<ApplicationDbContext> options)
        : TenancyArchitectureDbContext(options, new TenantContext())
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<TenantPrincipalProbe>().HasOne<ApplicationUser>().WithMany()
                .HasForeignKey(principal => principal.UserId).OnDelete(DeleteBehavior.ClientCascade);
        }
    }

    private sealed class ClientSetNullContext(DbContextOptions<ApplicationDbContext> options)
        : TenancyArchitectureDbContext(options, new TenantContext())
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<TenantPrincipalProbe>().HasOne(principal => principal.Guardian).WithMany()
                .HasForeignKey(principal => new { principal.ChurchId, principal.GuardianId }).OnDelete(DeleteBehavior.ClientSetNull);
        }
    }
}
