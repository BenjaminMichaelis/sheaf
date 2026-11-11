using System.Reflection;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sheaf.Data;
using Sheaf.Data.Tenancy;

using TUnit.Assertions.Enums;

using DomainCapture = Sheaf.Data.Capture;
using DomainContext = Sheaf.Data.Context;
using DomainTouchpoint = Sheaf.Data.Touchpoint;

namespace Sheaf.Core.Tests;

public class SqlServerMigrationIsolationTests
{
    [ClassDataSource<ProductionSqlServerFixture>]
    public required ProductionSqlServerFixture Sql { get; init; }

    [Test]
    public async Task ToListAsync_MigratedSchemaEveryTenantedSet_ReturnsOnlyCurrentChurchRows()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using AsyncServiceScope scopeA = Sql.CreateScope(seed.ChurchA.Church.Id);
        await using AsyncServiceScope scopeB = Sql.CreateScope(seed.ChurchB.Church.Id);
        ApplicationDbContext dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ApplicationDbContext dbB = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Type[] roots = dbA.Model.GetEntityTypes()
            .Where(entity => !entity.IsOwned() && entity.BaseType is null
                && typeof(ITenanted).IsAssignableFrom(entity.ClrType))
            .Select(entity => entity.ClrType).ToArray();
        PropertyInfo[] sets = typeof(ApplicationDbContext).GetProperties()
            .Where(property => property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                && typeof(ITenanted).IsAssignableFrom(property.PropertyType.GetGenericArguments()[0]))
            .ToArray();

        await Assert.That(roots).IsNotEmpty();
        await Assert.That(sets).Count().IsEqualTo(roots.Length);
        await Assert.That(sets.Select(property => property.PropertyType.GetGenericArguments()[0]))
            .IsEquivalentTo(roots);
        await Assert.That(seed.ChurchA.ExpectedIds.Keys).IsEquivalentTo(roots);
        await Assert.That(seed.ChurchB.ExpectedIds.Keys).IsEquivalentTo(roots);
        await Assert.That(await dbA.Churches.CountAsync()).IsEqualTo(2);
        await Assert.That(await dbA.Users.CountAsync()).IsEqualTo(2);

        MethodInfo query = typeof(SqlServerMigrationIsolationTests)
            .GetMethod(nameof(AssertTenantedSetAsync), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The production DbSet assertion method is missing.");
        foreach (PropertyInfo set in sets)
        {
            Type entityType = set.PropertyType.GetGenericArguments()[0];
            Task assertion = query.MakeGenericMethod(entityType).Invoke(null, [set, dbA, dbB, seed]) as Task
                ?? throw new InvalidOperationException($"Cannot query the production DbSet {set.Name}.");
            await assertion;
        }

        await AssertContextJsonAsync(dbA, seed.ChurchA.Context);
        await AssertContextJsonAsync(dbB, seed.ChurchB.Context);
    }

    [Test]
    public async Task SaveChangesAsync_MigratedSchemaOtherChurchOwnedJson_RejectsWriteAndPreservesSchema()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            DomainContext otherChurch = await db.Contexts.IgnoreQueryFilters()
                .SingleAsync(context => context.Id == seed.ChurchB.Context.Id);
            otherChurch.CustomFieldSchema.Fields[0].Name = "blocked-foreign-schema-edit";

            await Assert.That(async () => await db.SaveChangesAsync())
                .Throws<InvalidOperationException>()
                .WithMessageContaining("not the current church");
        }

        await using AsyncServiceScope verifyScope = Sql.CreateScope(seed.ChurchB.Church.Id);
        ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AssertContextJsonAsync(verify, seed.ChurchB.Context);
    }

    [Test]
    public async Task SaveChangesAsync_MigratedSchemaCrossChurchGuardian_ThrowsForeignKeyViolation()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id);
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Sheaf.Data.Person person = await db.People.SingleAsync();
        person.GuardianPersonId = seed.ChurchB.Person.Id;

        await AssertForeignKeyViolationAsync(db, "FK_People_People_ChurchId_GuardianPersonId");
    }

    [Test]
    public async Task ExecuteDeleteAsync_MigratedSchemaReferencedMembership_ThrowsRestrictViolation()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            SqlException? exception = await Assert.That(async () => await db.ChurchMemberships.ExecuteDeleteAsync())
                .Throws<SqlException>();
            await Assert.That(exception?.Number).IsEqualTo(547);
        }

        await using AsyncServiceScope verifyScope = Sql.CreateScope(seed.ChurchA.Church.Id);
        ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await verify.ChurchMemberships.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.ContextLeaders.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.Relationships.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task SaveChangesAsync_MigratedSchemaCrossChurchForeignKey_ThrowsForeignKeyViolation()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id);
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Touchpoints.Add(new DomainTouchpoint
        {
            ChurchId = seed.ChurchA.Church.Id,
            PersonId = seed.ChurchB.Person.Id,
            AuthorLeaderId = seed.ChurchA.Membership.Id,
            ContextId = seed.ChurchA.Context.Id,
            Note = "Synthetic cross-church reference",
            NoteVisibility = NoteVisibility.Private,
            OccurredAt = ProductionTwoChurchSeed.OccurredAt
        });

        await AssertForeignKeyViolationAsync(db, "FK_Touchpoints_People_ChurchId_PersonId");
    }

    [Test]
    public async Task SaveChangesAsync_MigratedSchemaOrphanChurchId_ThrowsForeignKeyViolation()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        Guid orphanChurchId = Guid.NewGuid();
        await using AsyncServiceScope scope = Sql.CreateScope(orphanChurchId);
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(db.ChurchIdOrThrow).IsEqualTo(orphanChurchId);
        await Assert.That(await db.Churches.AnyAsync(church => church.Id == orphanChurchId)).IsFalse();
        await Assert.That(await db.Users.AnyAsync(user => user.Id == seed.ChurchA.User.Id)).IsTrue();
        db.ChurchMemberships.Add(new ChurchMembership
        {
            ChurchId = orphanChurchId,
            UserId = seed.ChurchA.User.Id,
            Role = ChurchMembershipRole.Leader
        });

        await AssertForeignKeyViolationAsync(db, "FK_ChurchMemberships_Churches_ChurchId");
    }

    [Test]
    [Arguments("Relationship.LeaderId")]
    [Arguments("ContextLeader.LeaderId")]
    [Arguments("Attendance.RecordedByLeaderId")]
    public async Task SaveChangesAsync_MigratedSchemaCrossChurchMembershipLeader_ThrowsForeignKeyViolation(string leaderReference)
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id);
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (ITenanted Dependent, string Constraint) reference = leaderReference switch
        {
            "Relationship.LeaderId" => (new Relationship
            {
                ChurchId = seed.ChurchA.Church.Id,
                PersonId = seed.ChurchA.Person.Id,
                LeaderId = seed.ChurchB.Membership.Id,
                Kind = "fixture-follow-up",
                Status = RelationshipStatus.Open,
                StartedOn = ProductionTwoChurchSeed.OccurredAt
            }, "FK_Relationships_ChurchMemberships_ChurchId_LeaderId"),
            "ContextLeader.LeaderId" => (new ContextLeader
            {
                ChurchId = seed.ChurchA.Church.Id,
                ContextId = seed.ChurchA.Context.Id,
                LeaderId = seed.ChurchB.Membership.Id,
                StartedOn = ProductionTwoChurchSeed.OccurredAt
            }, "FK_ContextLeaders_ChurchMemberships_ChurchId_LeaderId"),
            "Attendance.RecordedByLeaderId" => (new Attendance
            {
                ChurchId = seed.ChurchA.Church.Id,
                ContextId = seed.ChurchA.Context.Id,
                PersonId = seed.ChurchA.Person.Id,
                RecordedByLeaderId = seed.ChurchB.Membership.Id,
                SessionDate = DateOnly.FromDateTime(ProductionTwoChurchSeed.OccurredAt.UtcDateTime)
            }, "FK_Attendances_ChurchMemberships_ChurchId_RecordedByLeaderId"),
            _ => throw new ArgumentOutOfRangeException(nameof(leaderReference), leaderReference, "Unknown production leader reference.")
        };
        db.Add(reference.Dependent);

        await AssertForeignKeyViolationAsync(db, reference.Constraint);
    }

    [Test]
    public async Task SaveChangesAsync_MigratedSchemaSameExternalIdInTwoChurches_Succeeds()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        const string sharedExternalId = "synthetic-shared-external-id";
        Dictionary<Guid, Guid> writtenIds = [];
        foreach (ProductionChurchSeed church in new[] { seed.ChurchA, seed.ChurchB })
        {
            PersonExternalRef reference = new()
            {
                ChurchId = church.Church.Id,
                PersonId = church.Person.Id,
                System = PersonExternalSystem.Csv,
                ExternalId = sharedExternalId,
                SyncedAt = ProductionTwoChurchSeed.OccurredAt
            };
            await using (AsyncServiceScope scope = Sql.CreateScope(church.Church.Id))
            {
                ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.PersonExternalRefs.Add(reference);
                await Assert.That(await db.SaveChangesAsync()).IsEqualTo(1);
            }

            writtenIds.Add(church.Church.Id, reference.Id);
        }

        foreach (ProductionChurchSeed church in new[] { seed.ChurchA, seed.ChurchB })
        {
            await using AsyncServiceScope verifyScope = Sql.CreateScope(church.Church.Id);
            ApplicationDbContext verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            PersonExternalRef persisted = await verify.PersonExternalRefs.AsNoTracking()
                .SingleAsync(row => row.System == PersonExternalSystem.Csv && row.ExternalId == sharedExternalId);
            await Assert.That(persisted.Id).IsEqualTo(writtenIds[church.Church.Id]);
            await Assert.That(persisted.ChurchId).IsEqualTo(church.Church.Id);
            await Assert.That(persisted.PersonId).IsEqualTo(church.Person.Id);
        }
    }

    [Test]
    public async Task SaveChangesAsync_MigratedSchemaDuplicateExternalIdInOneChurch_Throws()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.PersonExternalRefs.Add(new PersonExternalRef
            {
                ChurchId = seed.ChurchA.Church.Id,
                PersonId = seed.ChurchA.Person.Id,
                System = seed.ChurchA.ExternalRef.System,
                ExternalId = seed.ChurchA.ExternalRef.ExternalId
            });
            SqlException exception = await AssertSqlFailureAsync(db);
            await Assert.That(exception.Number).IsEqualTo(2601).Or.IsEqualTo(2627);
            await Assert.That(exception.Message).Contains("IX_PersonExternalRefs_ChurchId_System_ExternalId");
        }

        foreach (ProductionChurchSeed church in new[] { seed.ChurchA, seed.ChurchB })
        {
            await using AsyncServiceScope scope = Sql.CreateScope(church.Church.Id);
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            PersonExternalRef persisted = await db.PersonExternalRefs.AsNoTracking()
                .SingleAsync(row => row.System == church.ExternalRef.System && row.ExternalId == church.ExternalRef.ExternalId);
            await Assert.That(persisted.Id).IsEqualTo(church.ExternalRef.Id);
            await Assert.That(persisted.ChurchId).IsEqualTo(church.Church.Id);
        }
    }

    [Test]
    public async Task ExecuteUpdateAsync_MigratedSchema_OnlyAffectsCurrentChurchRows()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        const string updatedStatus = "updated-by-church-A";
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            int affected = await db.Captures.ExecuteUpdateAsync(
                rows => rows.SetProperty(row => row.Status, updatedStatus));
            await Assert.That(affected).IsEqualTo(1);
        }

        foreach (ProductionChurchSeed church in new[] { seed.ChurchA, seed.ChurchB })
        {
            await using AsyncServiceScope scope = Sql.CreateScope(church.Church.Id);
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            DomainCapture persisted = await db.Captures.AsNoTracking().SingleAsync();
            await Assert.That(persisted.Id).IsEqualTo(church.Capture.Id);
            await Assert.That(persisted.ChurchId).IsEqualTo(church.Church.Id);
            await Assert.That(persisted.Status).IsEqualTo(
                church == seed.ChurchA ? updatedStatus : church.Capture.Status);
            await Assert.That(persisted.RawText).IsEqualTo(church.Capture.RawText);
        }
    }

    [Test]
    public async Task ExecuteDeleteAsync_MigratedSchema_OnlyAffectsCurrentChurchRows()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            int affected = await db.Captures.ExecuteDeleteAsync();
            await Assert.That(affected).IsEqualTo(1);
        }

        await using AsyncServiceScope scopeA = Sql.CreateScope(seed.ChurchA.Church.Id);
        ApplicationDbContext dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await dbA.Captures.AsNoTracking().ToListAsync()).IsEmpty();

        await using AsyncServiceScope scopeB = Sql.CreateScope(seed.ChurchB.Church.Id);
        ApplicationDbContext dbB = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        DomainCapture remaining = await dbB.Captures.AsNoTracking().SingleAsync();
        await Assert.That(remaining.Id).IsEqualTo(seed.ChurchB.Capture.Id);
        await Assert.That(remaining.ChurchId).IsEqualTo(seed.ChurchB.Church.Id);
        await Assert.That(remaining.Status).IsEqualTo(seed.ChurchB.Capture.Status);
        await Assert.That(remaining.RawText).IsEqualTo(seed.ChurchB.Capture.RawText);
    }

    [Test]
    public async Task SaveChangesAsync_MigratedSchemaAddWithoutChurchId_StampsCurrentChurchId()
    {
        ProductionTwoChurchSeed seed = await ProductionTwoChurchSeed.SeedAsync(Sql);
        DomainCapture capture = new() { Status = "stamp-check" };
        Guid originalId = capture.Id;
        await Assert.That(capture.ChurchId).IsEqualTo(Guid.Empty);
        await using (AsyncServiceScope scope = Sql.CreateScope(seed.ChurchA.Church.Id))
        {
            ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Captures.Add(capture);
            await Assert.That(await db.SaveChangesAsync()).IsEqualTo(1);
            await Assert.That(capture.ChurchId).IsEqualTo(seed.ChurchA.Church.Id);
            await Assert.That(capture.Id).IsEqualTo(originalId);
        }

        await using AsyncServiceScope scopeA = Sql.CreateScope(seed.ChurchA.Church.Id);
        ApplicationDbContext dbA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        DomainCapture persisted = await dbA.Captures.AsNoTracking().SingleAsync(row => row.Id == originalId);
        await Assert.That(persisted.ChurchId).IsEqualTo(seed.ChurchA.Church.Id);
        await Assert.That(persisted.Status).IsEqualTo(capture.Status);

        await using AsyncServiceScope scopeB = Sql.CreateScope(seed.ChurchB.Church.Id);
        ApplicationDbContext dbB = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await dbB.Captures.AnyAsync(row => row.Id == originalId)).IsFalse();
        await Assert.That(await dbB.Captures.CountAsync()).IsEqualTo(1);
    }

    private static async Task AssertTenantedSetAsync<TEntity>(
        PropertyInfo property, ApplicationDbContext dbA, ApplicationDbContext dbB, ProductionTwoChurchSeed seed)
        where TEntity : class, ITenanted
    {
        foreach ((ApplicationDbContext db, ProductionChurchSeed church) in new[] { (dbA, seed.ChurchA), (dbB, seed.ChurchB) })
        {
            DbSet<TEntity> set = property.GetValue(db) as DbSet<TEntity>
                ?? throw new InvalidOperationException($"{property.Name} is not a production DbSet<{typeof(TEntity).Name}>.");
            List<TEntity> rows = await set.AsNoTracking().ToListAsync();
            await Assert.That(rows).IsNotEmpty().Because($"{property.Name} must have seeded rows for both churches.");
            await Assert.That(rows).Count().IsEqualTo(church.ExpectedIds[typeof(TEntity)].Length);
            await Assert.That(rows).All(row => row.ChurchId == church.Church.Id)
                .Because($"{property.Name} must be filtered to the active church.");
            await Assert.That(rows.Select(row => db.Entry(row).Property<Guid>("Id").CurrentValue))
                .IsEquivalentTo(church.ExpectedIds[typeof(TEntity)]);
        }
    }

    private static async Task AssertContextJsonAsync(ApplicationDbContext db, DomainContext expected)
    {
        DomainContext persisted = await db.Contexts.AsNoTracking().SingleAsync(row => row.Id == expected.Id);
        await Assert.That(persisted.Kind).IsEqualTo(ContextKind.Group);
        await Assert.That(persisted.CustomFieldSchema.Fields).IsNotEmpty();
        await Assert.That(persisted.CustomFieldSchema.Fields.Select(field => (field.Name, field.Type)))
            .IsEquivalentTo(expected.CustomFieldSchema.Fields.Select(field => (field.Name, field.Type)),
                CollectionOrdering.Matching);
    }

    private static async Task AssertForeignKeyViolationAsync(ApplicationDbContext db, string constraint)
    {
        SqlException exception = await AssertSqlFailureAsync(db);
        await Assert.That(exception.Number).IsEqualTo(547);
        await Assert.That(exception.Message).Contains(constraint);
    }

    private static async Task<SqlException> AssertSqlFailureAsync(ApplicationDbContext db)
    {
        DbUpdateException? exception = await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        await Assert.That(exception?.InnerException).IsTypeOf<SqlException>();
        return exception?.InnerException as SqlException
            ?? throw new InvalidOperationException("The failed production write did not return a SQL Server exception.");
    }
}
