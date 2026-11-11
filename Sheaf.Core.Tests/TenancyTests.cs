using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Sheaf.Data;
using Sheaf.Data.Tenancy;

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
            .UseMultiTenancy()
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
    public async Task ToListAsync_WithoutTenant_Throws()
    {
        using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .UseMultiTenancy()
            .Options;

        await using TenantTestDbContext dbContext = new(options);
        await dbContext.Database.EnsureCreatedAsync();

        await Assert.That(async () => await dbContext.TenantRecords.ToListAsync())
            .Throws<InvalidOperationException>()
            .WithMessage("A church tenant is required for this query.");
    }

    [Test]
    public async Task ChurchIdOrThrow_SeparateServiceScopes_ReturnsEachScopesChurchId()
    {
        using IHost host = CreateHost();
        using IServiceScope scopeA = host.Services.CreateScope();
        using IServiceScope scopeB = host.Services.CreateScope();

        Guid churchIdA = Guid.NewGuid();
        Guid churchIdB = Guid.NewGuid();
        Guid leaderIdA = Guid.NewGuid();
        Guid leaderIdB = Guid.NewGuid();
        scopeA.ServiceProvider.GetRequiredService<TenantContext>().ChurchId = churchIdA;
        scopeB.ServiceProvider.GetRequiredService<TenantContext>().ChurchId = churchIdB;
        scopeA.ServiceProvider.GetRequiredService<TenantContext>().LeaderId = leaderIdA;
        scopeB.ServiceProvider.GetRequiredService<TenantContext>().LeaderId = leaderIdB;
        ApplicationDbContext contextA = scopeA.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ApplicationDbContext contextB = scopeB.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.That(scopeA.ServiceProvider.GetRequiredService<ICurrentChurch>().ChurchId).IsEqualTo(churchIdA);
        await Assert.That(scopeB.ServiceProvider.GetRequiredService<ICurrentChurch>().ChurchId).IsEqualTo(churchIdB);
        await Assert.That(scopeA.ServiceProvider.GetRequiredService<ICurrentChurch>().LeaderId).IsEqualTo(leaderIdA);
        await Assert.That(scopeB.ServiceProvider.GetRequiredService<ICurrentChurch>().LeaderId).IsEqualTo(leaderIdB);
        await Assert.That(contextA.ChurchIdOrThrow).IsEqualTo(churchIdA);
        await Assert.That(contextB.ChurchIdOrThrow).IsEqualTo(churchIdB);
        await Assert.That(ReferenceEquals(contextA, contextB)).IsFalse();
    }

    [Test]
    public async Task ChurchIdOrThrow_RequestWithoutSelectedChurch_Throws()
    {
        using IHost host = CreateHost();
        using IServiceScope scope = host.Services.CreateScope();
        ICurrentChurch currentChurch = scope.ServiceProvider.GetRequiredService<ICurrentChurch>();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.That(currentChurch.ChurchId).IsNull();
        await Assert.That(currentChurch.LeaderId).IsNull();
        await Assert.That(() => context.ChurchIdOrThrow)
            .Throws<InvalidOperationException>()
            .WithMessage("A church tenant is required for this query.");
    }

    [Test]
    public async Task Model_AddDatabaseOptions_EnforcesTenancyAndIdentitySchema()
    {
        using IHost host = CreateHost();
        using IServiceScope scope = host.Services.CreateScope();
        DbContextOptions<ApplicationDbContext> options =
            scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        await using TenantTestDbContext context = new(options);

        await Assert.That(options.FindExtension<MultiTenancyExtension>()).IsNotNull();
        await Assert.That(context.Database.ProviderName).IsEqualTo("Microsoft.EntityFrameworkCore.SqlServer");
        IEntityType probe = context.Model.FindEntityType(typeof(TenantRecord))
            ?? throw new InvalidOperationException("The production-options model is missing the tenanted probe.");

        await Assert.That(probe.FindDeclaredQueryFilter(MultiTenancyConvention.FilterName)).IsNotNull();
        await Assert.That(probe.FindDeclaredQueryFilter(null)).IsNull();
        await Assert.That(probe.GetDeclaredQueryFilters()).Count().IsEqualTo(1);
        IKey key = probe.FindPrimaryKey()
            ?? throw new InvalidOperationException("The tenanted probe is missing its primary key.");
        await Assert.That(key.Properties.Count).IsEqualTo(2);
        await Assert.That(key.Properties[0].Name).IsEqualTo(nameof(TenantRecord.ChurchId));
        await Assert.That(key.Properties[1].Name).IsEqualTo(nameof(TenantRecord.Id));

        IdentityOptions identityOptions = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        await Assert.That(identityOptions.Stores.SchemaVersion).IsEqualTo(IdentitySchemaVersions.Version3);
        await Assert.That(identityOptions.SignIn.RequireConfirmedAccount).IsTrue();
        await Assert.That(context.Model.FindEntityType(typeof(IdentityUserPasskey<string>))).IsNotNull();
    }

    [Test]
    public async Task OnConfiguring_AddDatabaseOptions_RegistersOneTenancyInterceptor()
    {
        using IHost host = CreateHost();
        using IServiceScope scope = host.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        IDbContextOptions configuredOptions = context.GetService<IDbContextOptions>();
        IEnumerable<TenancySaveChangesInterceptor> interceptors =
            configuredOptions.FindExtension<CoreOptionsExtension>()?.Interceptors?
                .OfType<TenancySaveChangesInterceptor>() ?? [];

        await Assert.That(interceptors).Count().IsEqualTo(1);
        IEnumerable<TenancyQueryExpressionInterceptor> queryInterceptors =
            configuredOptions.FindExtension<CoreOptionsExtension>()?.Interceptors?
                .OfType<TenancyQueryExpressionInterceptor>() ?? [];
        await Assert.That(queryInterceptors).Count().IsEqualTo(1);
    }

    [Test]
    public async Task OnConfiguring_WithoutTenancyExtension_Throws()
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        await using ApplicationDbContext context = new(options);

        await Assert.That(() => context.Model)
            .Throws<InvalidOperationException>()
            .WithMessage("ApplicationDbContext requires UseMultiTenancy. Configure production services with AddSheafDatabase.");
    }

    private static IHost CreateHost() => ApplicationDbContextProbe.CreateHost();
}
