using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using TUnit.Assertions.Enums;

namespace Sheaf.Core.Tests;

[NotInParallel("sql-server")]
public class SqlServerTenancyMigrationTests
{
    [ClassDataSource<SqlServerFixture>(Shared = SharedType.PerAssembly)]
    public required SqlServerFixture Sql { get; init; }

    [Before(Test)]
    public async Task ResetDatabaseAsync() => await Sql.ResetAsync();

    [Test]
    public async Task MigrateAsync_FreshTestOnlySqlServerCatalog_AppliesEveryMigrationInOrder()
    {
        string connectionString;
        await using (TenancyTestDbContext existing = Sql.CreateContext(null))
        {
            string containerConnectionString = existing.Database.GetConnectionString()
                ?? throw new InvalidOperationException("The SQL Server fixture has no connection string.");
            connectionString = new SqlConnectionStringBuilder(containerConnectionString)
            {
                InitialCatalog = $"SheafTenancyMigration_{Guid.NewGuid():N}",
            }.ConnectionString;
        }

        DbContextOptions<TenancyTestDbContext> options =
            TenancyTestDbContext.BuildOptions(builder => builder.UseAzureSql(connectionString));
        await using TenancyTestDbContext db = new(options, churchId: null);
        try
        {
            string[] migrations = db.Database.GetMigrations().ToArray();
            await Assert.That(migrations).IsNotEmpty();
            await Assert.That(await db.Database.GetAppliedMigrationsAsync()).IsEmpty();

            await db.Database.MigrateAsync();

            string[] appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            await Assert.That(appliedMigrations).IsEquivalentTo(migrations, CollectionOrdering.Matching);
            await Assert.That(await db.Database.GetPendingMigrationsAsync()).IsEmpty();
            await Assert.That(await db.Set<GuardRecord>().IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
