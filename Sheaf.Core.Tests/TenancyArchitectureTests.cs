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
        IEntityType[] entities = fixture.RuntimeContext.Model.GetEntityTypes().ToArray();

        await Assert.That(entities.Where(IsTenanted)).IsNotEmpty();
        List<string> violations = GetArchitectureViolations(fixture.RuntimeContext.Model);

        await Assert.That(violations).IsEmpty().Because(string.Join(Environment.NewLine, violations));
    }

    [Test]
    public async Task GetForeignKeys_TenantedDependents_UseRestrictOrNoAction()
    {
        using TenancyArchitectureFixture fixture = new();
        IForeignKey[] foreignKeys = fixture.RuntimeContext.Model.GetEntityTypes()
            .Where(IsTenanted).SelectMany(entity => entity.GetForeignKeys()).ToArray();

        await Assert.That(foreignKeys).IsNotEmpty();
        await Assert.That(foreignKeys.Where(foreignKey =>
                foreignKey.DeleteBehavior is not (DeleteBehavior.Restrict or DeleteBehavior.NoAction))
            .Select(foreignKey =>
                $"{foreignKey.DeclaringEntityType.DisplayName()} -> {foreignKey.PrincipalEntityType.DisplayName()}: {foreignKey.DeleteBehavior}"))
            .IsEmpty();
    }

    [Test]
    public async Task GetForeignKeys_TenantedRoots_IncludeRequiredChurchForeignKey()
    {
        using TenancyArchitectureFixture fixture = new();
        IEntityType[] tenanted = fixture.RuntimeContext.Model.GetEntityTypes().Where(IsTenanted).ToArray();

        await Assert.That(tenanted).IsNotEmpty();
        foreach (IEntityType entity in tenanted)
        {
            IProperty? churchId = entity.FindProperty(nameof(ITenanted.ChurchId));
            await Assert.That(entity.GetForeignKeys().Any(foreignKey =>
                    foreignKey.PrincipalEntityType.ClrType == typeof(Church)
                    && foreignKey.IsRequired
                    && foreignKey.PrincipalKey.IsPrimaryKey()
                    && foreignKey.Properties.Count == 1
                    && churchId is not null && foreignKey.Properties[0] == churchId
                    && foreignKey.PrincipalKey.Properties.Count == 1
                    && foreignKey.PrincipalKey.Properties[0].Name == nameof(Church.Id)
                    && foreignKey.DeleteBehavior is DeleteBehavior.Restrict or DeleteBehavior.NoAction))
                .IsTrue().Because($"{entity.DisplayName()} must reference its owning Church.");
        }
    }

    [Test]
    public async Task GetEntityTypes_SharedEntityAllowlist_ExistsInProductionModel()
    {
        using TenancyArchitectureFixture fixture = new();
        HashSet<Type> productionTypes = fixture.RuntimeContext.Model.GetEntityTypes()
            .Select(entity => entity.ClrType).ToHashSet();

        await Assert.That(SharedEntityTypes.Where(type => !productionTypes.Contains(type))).IsEmpty();
    }

    [Test]
    public async Task GetEntityTypes_ProductionMembership_UsesChurchScopedKeyAndUniqueUserIndex()
    {
        using TenancyArchitectureFixture fixture = new();
        IEntityType membership = fixture.RuntimeContext.Model.FindEntityType(typeof(ChurchMembership))
            ?? throw new InvalidOperationException("ChurchMembership is missing from the production model.");
        IKey primaryKey = membership.FindPrimaryKey()
            ?? throw new InvalidOperationException("ChurchMembership has no primary key.");
        IForeignKey userForeignKey = membership.GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(ApplicationUser));

        await Assert.That(primaryKey.Properties.Select(property => property.Name)).IsEquivalentTo(
            [nameof(ChurchMembership.ChurchId), nameof(ChurchMembership.Id)], CollectionOrdering.Matching);
        await Assert.That(membership.GetIndexes().Any(index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(ChurchMembership.ChurchId), nameof(ChurchMembership.UserId)])))
            .IsTrue();
        await Assert.That(userForeignKey.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(ChurchMembership.UserId)]);
        await Assert.That(userForeignKey.IsRequired).IsTrue();
        await Assert.That(userForeignKey.PrincipalKey.IsPrimaryKey()).IsTrue();
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
}
