using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheaf.Core;
using Sheaf.Data;

namespace Sheaf.Core.Tests;

public class TenancyTests
{
    [Test]
    public async Task Church_HasGeneratedIdAndUniqueSlug()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new TenantTestDbContext(options, new TenantContext());
        var church = new Church { Slug = "test-church", Name = "Test Church" };
        var slugIndex = dbContext.Model.FindEntityType(typeof(Church))!
            .GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == nameof(Church.Slug)));

        await Assert.That(church.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(slugIndex.IsUnique).IsTrue();
    }

    [Test]
    public async Task NoTenant_Query_Throws()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new TenantTestDbContext(options, new TenantContext());
        await dbContext.Database.EnsureCreatedAsync();

        await Assert.That(async () => await dbContext.TenantRecords.ToListAsync())
            .Throws<InvalidOperationException>()
            .WithMessage("A church tenant is required for this query.");
    }

    [Test]
    public async Task TenantContext_IsIsolatedAcrossServiceScopes()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"ConnectionStrings:{ConnectionStrings.DatabaseKey}"] =
            "Server=localhost;Database=Sheaf;";

        builder.AddDatabase();

        using var host = builder.Build();
        using var scopeA = host.Services.CreateScope();
        using var scopeB = host.Services.CreateScope();

        var churchIdA = Guid.NewGuid();
        var churchIdB = Guid.NewGuid();
        scopeA.ServiceProvider.GetRequiredService<TenantContext>().ChurchId = churchIdA;
        scopeB.ServiceProvider.GetRequiredService<TenantContext>().ChurchId = churchIdB;
        var contextA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.That(contextA.ChurchIdOrThrow).IsEqualTo(churchIdA);
        await Assert.That(contextB.ChurchIdOrThrow).IsEqualTo(churchIdB);
        await Assert.That(ReferenceEquals(contextA, contextB)).IsFalse();
    }

    private sealed class TenantTestDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext)
        : ApplicationDbContext(options, tenantContext)
    {
        public DbSet<TenantRecord> TenantRecords => Set<TenantRecord>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<TenantRecord>();
            base.OnModelCreating(builder);
        }
    }

    private sealed class TenantRecord : ITenanted
    {
        public int Id { get; set; }

        public Guid ChurchId { get; set; }
    }
}
