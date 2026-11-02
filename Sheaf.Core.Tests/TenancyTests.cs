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
    public async Task SaveChangesAsync_DuplicateChurchSlug_Throws()
    {
        using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using TenantTestDbContext dbContext = new(options, new TenantContext());
        await dbContext.Database.EnsureCreatedAsync();

        Church church = new() { Slug = "test-church", Name = "Test Church" };
        dbContext.Churches.Add(church);
        await dbContext.SaveChangesAsync();
        await Assert.That(church.Id).IsNotEqualTo(Guid.Empty);

        Church duplicateChurch = new() { Slug = church.Slug, Name = "Duplicate Church" };
        dbContext.Churches.Add(duplicateChurch);

        await Assert.That(async () => await dbContext.SaveChangesAsync())
            .Throws<DbUpdateException>();
    }

    [Test]
    public async Task Query_WithoutTenant_Throws()
    {
        using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using TenantTestDbContext dbContext = new(options, new TenantContext());
        await dbContext.Database.EnsureCreatedAsync();

        await Assert.That(async () => await dbContext.TenantRecords.ToListAsync())
            .Throws<InvalidOperationException>()
            .WithMessage("A church tenant is required for this query.");
    }

    [Test]
    public async Task ChurchIdOrThrow_SeparateServiceScopes_ReturnsEachScopesChurchId()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"ConnectionStrings:{ConnectionStrings.DatabaseKey}"] =
            "Server=localhost;Database=Sheaf;";

        builder.AddDatabase();

        using IHost host = builder.Build();
        using IServiceScope scopeA = host.Services.CreateScope();
        using IServiceScope scopeB = host.Services.CreateScope();

        Guid churchIdA = Guid.NewGuid();
        Guid churchIdB = Guid.NewGuid();
        scopeA.ServiceProvider.GetRequiredService<TenantContext>().ChurchId = churchIdA;
        scopeB.ServiceProvider.GetRequiredService<TenantContext>().ChurchId = churchIdB;
        ApplicationDbContext contextA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ApplicationDbContext contextB = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();

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
