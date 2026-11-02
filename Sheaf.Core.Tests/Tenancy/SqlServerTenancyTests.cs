using System.Reflection;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

// The container's database is shared, so tests that reset and seed it must not overlap.
[NotInParallel("sql-server")]
public class SqlServerTenancyTests
{
    [ClassDataSource<SqlServerFixture>(Shared = SharedType.PerAssembly)]
    public required SqlServerFixture Sql { get; init; }

    [Before(Test)]
    public async Task ResetDatabaseAsync() => await Sql.ResetAsync();

    [Test]
    public async Task Read_ChurchA_NeverSeesChurchB()
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);
        Guid churchA = seed.ChurchA.Id;

        await using TenancyTestDbContext db = Sql.CreateContext(churchA);
        PropertyInfo[] dbSets = typeof(TenancyTestDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .ToArray();

        Dictionary<Type, Guid[]> expectedIds = new()
        {
            [typeof(Leader)] = [seed.LeaderA.Id],
            [typeof(Person)] = [.. seed.PeopleA.Select(p => p.Id)],
        };
        int tenantedSets = 0;
        foreach (PropertyInfo dbSet in dbSets)
        {
            Type entityType = dbSet.PropertyType.GetGenericArguments()[0];
            if (!typeof(ITenanted).IsAssignableFrom(entityType))
            {
                continue;
            }

            tenantedSets++;
            MethodInfo method = typeof(SqlServerTenancyTests)
                .GetMethod(nameof(GetChurchIdsAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(entityType);
            (List<Guid> visible, int total) = await (Task<(List<Guid>, int)>)method.Invoke(null, [db])!;

            await Assert.That(visible).IsNotEmpty();
            await Assert.That(visible.Distinct()).IsEquivalentTo([churchA]);
            await Assert.That(total).IsGreaterThan(visible.Count);
        }

        await Assert.That(tenantedSets).IsEqualTo(expectedIds.Count);
    }

    [Test]
    public async Task CrossTenantFk_ThrowsOnSave()
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);

        await using TenancyTestDbContext db = Sql.CreateContext(seed.ChurchA.Id);
        db.People.Add(new Person
        {
            Id = Guid.NewGuid(),
            ChurchId = seed.ChurchA.Id,
            Name = "Crossed",
            Email = "crossed@example.com",
            LeaderId = seed.LeaderB.Id,
        });

        DbUpdateException? exception = await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        await Assert.That((exception?.InnerException as SqlException)?.Number).IsEqualTo(547);
    }

    [Test]
    public async Task UniqueIndex_AllowsSameValueInTwoChurches()
    {
        TwoChurchSeed seed = await TwoChurchSeed.SeedAsync(Sql.CreateContext);
        const string email = "shared@example.com";

        foreach (Guid churchId in new[] { seed.ChurchA.Id, seed.ChurchB.Id })
        {
            await using TenancyTestDbContext db = Sql.CreateContext(churchId);
            db.People.Add(new Person { Id = Guid.NewGuid(), ChurchId = churchId, Name = "Shared", Email = email });
            await db.SaveChangesAsync();
        }

        await using TenancyTestDbContext duplicateDb = Sql.CreateContext(seed.ChurchA.Id);
        duplicateDb.People.Add(new Person { Id = Guid.NewGuid(), ChurchId = seed.ChurchA.Id, Name = "Dup", Email = email });
        await Assert.That(async () => await duplicateDb.SaveChangesAsync()).Throws<DbUpdateException>();

        await using TenancyTestDbContext verify = Sql.CreateContext(null);
        await Assert.That(await verify.People.IgnoreQueryFilters().CountAsync(p => p.Email == email)).IsEqualTo(2);
    }

    private static async Task<(List<Guid>, int)> GetChurchIdsAsync<TEntity>(TenancyTestDbContext db)
        where TEntity : class, ITenanted
    {
        List<Guid> visible = await db.Set<TEntity>().Select(e => e.ChurchId).ToListAsync();
        int total = await db.Set<TEntity>().IgnoreQueryFilters().CountAsync();
        return (visible, total);
    }
}
