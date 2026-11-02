using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Sheaf.Data.Tenancy;

using Church = Sheaf.Data.Church;

namespace Sheaf.Core.Tests;

public class TenancyArchitectureTests
{
    private static readonly HashSet<string> SharedEntityNames =
    [
        nameof(Church),
        "LeaderToken",
        "DataProtectionKeys",
    ];

    [Test]
    public async Task GetEntityTypes_AllEntities_ConformToTenancyArchitecture()
    {
        using SqliteFixture sqlite = new();
        await using TenancyTestDbContext db = sqlite.CreateContext(Guid.NewGuid());

        List<string> violations = [];
        foreach (IEntityType entityType in db.Model.GetEntityTypes())
        {
            bool isTenanted = typeof(ITenanted).IsAssignableFrom(entityType.ClrType);
            if (!isTenanted
                && !SharedEntityNames.Contains(entityType.Name)
                && !SharedEntityNames.Contains(entityType.ClrType.Name))
            {
                violations.Add($"{entityType.DisplayName()} is neither ITenanted nor an allowed shared entity.");
            }

            if (isTenanted)
            {
                if (!entityType.GetDeclaredQueryFilters().Any(filter => filter.Key == MultiTenancyConvention.FilterName))
                {
                    violations.Add($"{entityType.DisplayName()} does not declare the {MultiTenancyConvention.FilterName} query filter.");
                }

                IKey? primaryKey = entityType.FindPrimaryKey();
                if (primaryKey is null
                    || primaryKey.Properties.Count == 0
                    || primaryKey.Properties[0].Name != nameof(ITenanted.ChurchId))
                {
                    violations.Add($"{entityType.DisplayName()} does not have ChurchId as the first primary-key property.");
                }

                foreach (IIndex index in entityType.GetIndexes().Where(index => index.IsUnique))
                {
                    if (!index.Properties.Any(property => property.Name == nameof(ITenanted.ChurchId)))
                    {
                        violations.Add($"{entityType.DisplayName()} has a unique index without ChurchId.");
                    }
                }
            }

            foreach (IForeignKey foreignKey in entityType.GetForeignKeys()
                         .Where(foreignKey => typeof(ITenanted).IsAssignableFrom(foreignKey.PrincipalEntityType.ClrType)))
            {
                if (!foreignKey.Properties.Any(property => property.Name == nameof(ITenanted.ChurchId)))
                {
                    violations.Add($"{entityType.DisplayName()} has a foreign key to tenanted {foreignKey.PrincipalEntityType.DisplayName()} without ChurchId.");
                }
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task ExecuteUpdateDelete_RespectTenantFilter()
    {
        using SqliteFixture sqlite = new();
        Guid churchA = Guid.NewGuid();
        Guid churchB = Guid.NewGuid();
        Guid personAId = Guid.NewGuid();
        Guid personBId = Guid.NewGuid();

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
}
