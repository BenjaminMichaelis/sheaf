using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

        string? exceptionMessage = null;
        try
        {
            await dbContext.TenantRecords.ToListAsync();
        }
        catch (InvalidOperationException exception)
        {
            exceptionMessage = exception.Message;
        }

        await Assert.That(exceptionMessage)
            .IsEqualTo("A church tenant is required for this query.");
    }

    [Test]
    public async Task AddDatabase_RegistersScopedNonPooledContext()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"ConnectionStrings:{ConnectionStrings.DatabaseKey}"] =
            "Server=localhost;Database=Sheaf;";

        builder.AddDatabase();

        var context = builder.Services.Single(
            descriptor => descriptor.ServiceType == typeof(ApplicationDbContext));
        var tenantContext = builder.Services.Single(
            descriptor => descriptor.ServiceType == typeof(ITenantContext));
        var contextFactory = builder.Services.Any(
            descriptor => descriptor.ServiceType == typeof(IDbContextFactory<ApplicationDbContext>));

        await Assert.That(context.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(tenantContext.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(contextFactory).IsFalse();
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
