using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheaf.Data;
using Sheaf.Data.Tenancy;

using TUnit.Assertions.Enums;

using DomainCapture = Sheaf.Data.Capture;
using DomainContext = Sheaf.Data.Context;
using DomainPerson = Sheaf.Data.Person;
using DomainTouchpoint = Sheaf.Data.Touchpoint;

namespace Sheaf.Core.Tests;

public class ProductionDomainModelTests
{
    [Test]
    public async Task GetEntityTypes_ProductionDomainModel_ContainsEveryExpectedTenantedRootAndDbSet()
    {
        using IHost host = CreateHost();
        using IServiceScope scope = host.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Type[] expected =
        [
            typeof(ChurchMembership), typeof(DomainContext), typeof(DomainPerson), typeof(DomainCapture),
            typeof(DomainTouchpoint), typeof(Proposal), typeof(AuditEntry), typeof(LeaderToken),
            typeof(Relationship), typeof(ContextLeader), typeof(ContextMember), typeof(Attendance),
            typeof(PersonExternalRef)
        ];
        Type[] roots = db.Model.GetEntityTypes()
            .Where(entity => !entity.IsOwned() && typeof(ITenanted).IsAssignableFrom(entity.ClrType))
            .Select(entity => entity.ClrType).ToArray();
        Type[] sets = typeof(ApplicationDbContext).GetProperties()
            .Where(property => property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(property => property.PropertyType.GetGenericArguments()[0])
            .Where(type => typeof(ITenanted).IsAssignableFrom(type)).ToArray();

        await Assert.That(roots).IsEquivalentTo(expected);
        await Assert.That(sets).IsEquivalentTo(expected);
    }

    [Test]
    public async Task GetKeys_ChurchMembership_UsesAcceptedTenantKeyUniqueUserAndRequiredGlobalForeignKeys()
    {
        using IHost host = CreateHost();
        using IServiceScope scope = host.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IEntityType membership = GetEntityType(db.Model, typeof(ChurchMembership));
        IKey key = membership.FindPrimaryKey()
            ?? throw new InvalidOperationException("ChurchMembership has no primary key.");

        await Assert.That(key.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(ChurchMembership.ChurchId), nameof(ChurchMembership.Id)], CollectionOrdering.Matching);
        await Assert.That(key.Properties.Select(property => property.ClrType))
            .IsEquivalentTo([typeof(Guid), typeof(Guid)], CollectionOrdering.Matching);

        IIndex userIndex = membership.GetIndexes().Single(index => index.IsUnique);
        await Assert.That(userIndex.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(ChurchMembership.ChurchId), nameof(ChurchMembership.UserId)], CollectionOrdering.Matching);

        IForeignKey church = membership.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Church));
        IForeignKey user = membership.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(ApplicationUser));
        await Assert.That(membership.GetForeignKeys()).Count().IsEqualTo(2);
        await Assert.That(church.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(ChurchMembership.ChurchId)]);
        await Assert.That(user.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(ChurchMembership.UserId)]);

        foreach (IForeignKey foreignKey in new[] { church, user })
        {
            await Assert.That(foreignKey.IsRequired).IsTrue();
            await Assert.That(foreignKey.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
            await Assert.That(foreignKey.PrincipalKey.IsPrimaryKey()).IsTrue();
            await Assert.That(foreignKey.PrincipalKey.Properties.Select(property => property.Name))
                .IsEquivalentTo(["Id"]);
        }

        await Assert.That(user.Properties[0].ClrType).IsEqualTo(typeof(string));
        await Assert.That(user.Properties[0].IsNullable).IsFalse();
    }

    [Test]
    [Arguments(typeof(Relationship), nameof(Relationship.LeaderId))]
    [Arguments(typeof(ContextLeader), nameof(ContextLeader.LeaderId))]
    [Arguments(typeof(Attendance), nameof(Attendance.RecordedByLeaderId))]
    [Arguments(typeof(LeaderToken), nameof(LeaderToken.LeaderId))]
    [Arguments(typeof(DomainTouchpoint), nameof(DomainTouchpoint.AuthorLeaderId))]
    public async Task GetForeignKeys_ProductionLeaderReference_UsesRequiredCompositeMembershipPrimaryKey(
        Type dependentType, string leaderProperty)
    {
        using IHost host = CreateHost();
        using IServiceScope scope = host.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IEntityType dependent = GetEntityType(db.Model, dependentType);
        IForeignKey foreignKey = dependent.GetForeignKeys()
            .Single(fk => fk.Properties.Any(property => property.Name == leaderProperty));

        await Assert.That(foreignKey.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(ITenanted.ChurchId), leaderProperty], CollectionOrdering.Matching);
        await Assert.That(foreignKey.PrincipalEntityType.ClrType).IsEqualTo(typeof(ChurchMembership));
        await Assert.That(foreignKey.PrincipalKey.IsPrimaryKey()).IsTrue();
        await Assert.That(foreignKey.PrincipalKey.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(ChurchMembership.ChurchId), nameof(ChurchMembership.Id)], CollectionOrdering.Matching);
        await Assert.That(foreignKey.Properties[1].ClrType).IsEqualTo(typeof(Guid));
        await Assert.That(foreignKey.IsRequired).IsTrue();
        await Assert.That(foreignKey.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
    }

    [Test]
    [Arguments(typeof(DomainPerson), nameof(DomainPerson.GuardianPersonId))]
    [Arguments(typeof(DomainTouchpoint), nameof(DomainTouchpoint.ContextId))]
    public async Task GetForeignKeys_OptionalProductionReference_KeepsChurchIdRequiredAndTargetIdNullable(
        Type dependentType, string targetProperty)
    {
        using IHost host = CreateHost();
        using IServiceScope scope = host.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IEntityType dependent = GetEntityType(db.Model, dependentType);
        IForeignKey foreignKey = dependent.GetForeignKeys()
            .Single(fk => fk.Properties.Any(property => property.Name == targetProperty));

        await Assert.That(foreignKey.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(ITenanted.ChurchId), targetProperty], CollectionOrdering.Matching);
        await Assert.That(foreignKey.IsRequired).IsFalse();
        await Assert.That(foreignKey.Properties[0].IsNullable).IsFalse();
        await Assert.That(foreignKey.Properties[1].IsNullable).IsTrue();
        await Assert.That(foreignKey.PrincipalKey.IsPrimaryKey()).IsTrue();
        await Assert.That(foreignKey.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
    }

    [Test]
    public async Task GetEntityTypes_ContextCustomFieldSchema_UsesOwnedJsonAndCompleteOwnerPrimaryKey()
    {
        using IHost host = CreateHost();
        using IServiceScope scope = host.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IModel model = db.GetService<IDesignTimeModel>().Model;
        IEntityType schema = GetEntityType(model, typeof(CustomFieldSchema));
        IEntityType definition = GetEntityType(model, typeof(CustomFieldDefinition));
        IForeignKey ownership = schema.FindOwnership()
            ?? throw new InvalidOperationException("CustomFieldSchema has no Context ownership.");

        await Assert.That(schema.IsOwned()).IsTrue();
        await Assert.That(schema.IsMappedToJson()).IsTrue();
        await Assert.That(ownership.PrincipalEntityType.ClrType).IsEqualTo(typeof(DomainContext));
        await Assert.That(ownership.PrincipalKey.IsPrimaryKey()).IsTrue();
        await Assert.That(ownership.PrincipalKey.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(DomainContext.ChurchId), nameof(DomainContext.Id)], CollectionOrdering.Matching);
        await Assert.That(definition.IsOwned()).IsTrue();
        await Assert.That(definition.IsMappedToJson()).IsTrue();
        await Assert.That(schema.GetTableName()).IsEqualTo("Contexts");
        await Assert.That(definition.GetTableName()).IsEqualTo("Contexts");
    }

    [Test]
    public async Task GetIndexes_PersonExternalRef_UniquelyScopesExternalIdsWithoutUsingThemAsKeys()
    {
        using IHost host = CreateHost();
        using IServiceScope scope = host.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IEntityType externalRef = GetEntityType(db.Model, typeof(PersonExternalRef));
        IIndex index = externalRef.GetIndexes().Single(candidate => candidate.IsUnique);
        IKey key = externalRef.FindPrimaryKey()
            ?? throw new InvalidOperationException("PersonExternalRef has no primary key.");

        await Assert.That(index.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(PersonExternalRef.ChurchId), nameof(PersonExternalRef.System),
                nameof(PersonExternalRef.ExternalId)], CollectionOrdering.Matching);
        await Assert.That(key.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(PersonExternalRef.ChurchId), nameof(PersonExternalRef.Id)], CollectionOrdering.Matching);
        await Assert.That(externalRef.GetForeignKeys().SelectMany(fk => fk.Properties)
            .Any(property => property.Name == nameof(PersonExternalRef.ExternalId))).IsFalse();
        await Assert.That(GetEntityType(db.Model, typeof(DomainPerson))
            .FindProperty(nameof(PersonExternalRef.ExternalId))).IsNull();
    }

    private static IHost CreateHost() => ProductionDatabaseHost.Create(
        "Server=127.0.0.1,1;Database=SheafDomainModelTests;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=1");

    private static IEntityType GetEntityType(IModel model, Type type) => model.FindEntityType(type)
        ?? throw new InvalidOperationException($"The production model is missing {type.Name}.");
}
