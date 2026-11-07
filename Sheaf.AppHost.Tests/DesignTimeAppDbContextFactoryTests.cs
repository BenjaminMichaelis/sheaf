using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using Sheaf.Core;
using Sheaf.Data;
using Sheaf.Data.Tenancy;

namespace Sheaf.AppHost.Tests;

public class DesignTimeAppDbContextFactoryTests
{
    [Test]
    public async Task CreateDbContext_ProductionOptions_EnforcesTenancyAndIdentitySchema()
    {
        await using ApplicationDbContext factoryContext = CreateContext();
        DbContextOptions<ApplicationDbContext> options =
            (DbContextOptions<ApplicationDbContext>)factoryContext.GetService<IDbContextOptions>();
        await using TenantProbeDbContext context = new(options);

        await Assert.That(options.FindExtension<MultiTenancyExtension>()).IsNotNull();
        await Assert.That(context.Database.ProviderName).IsEqualTo("Microsoft.EntityFrameworkCore.SqlServer");
        IEntityType probe = context.Model.FindEntityType(typeof(TenantProbe))
            ?? throw new InvalidOperationException("The design-time-options model is missing the tenanted probe.");

        await Assert.That(probe.FindDeclaredQueryFilter(MultiTenancyConvention.FilterName)).IsNotNull();
        await Assert.That(probe.FindDeclaredQueryFilter(null)).IsNull();
        await Assert.That(probe.GetDeclaredQueryFilters()).Count().IsEqualTo(1);
        IKey key = probe.FindPrimaryKey()
            ?? throw new InvalidOperationException("The tenanted probe is missing its primary key.");
        await Assert.That(key.Properties.Count).IsEqualTo(2);
        await Assert.That(key.Properties[0].Name).IsEqualTo(nameof(TenantProbe.ChurchId));
        await Assert.That(key.Properties[1].Name).IsEqualTo(nameof(TenantProbe.Id));
        await Assert.That(context.Model.FindEntityType(typeof(IdentityUserPasskey<string>))).IsNotNull();
    }

    [Test]
    public async Task CreateDbContext_AfterOnConfiguring_RegistersOneTenancyInterceptor()
    {
        await using ApplicationDbContext context = CreateContext();

        IDbContextOptions configuredOptions = context.GetService<IDbContextOptions>();
        IEnumerable<TenancySaveChangesInterceptor> interceptors =
            configuredOptions.FindExtension<CoreOptionsExtension>()?.Interceptors?
                .OfType<TenancySaveChangesInterceptor>() ?? [];

        await Assert.That(interceptors).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ChurchIdOrThrow_DesignTimeContextWithoutChurch_Throws()
    {
        await using ApplicationDbContext context = CreateContext();

        await Assert.That(context.Model.FindEntityType(typeof(Church))).IsNotNull();
        await Assert.That(() => context.ChurchIdOrThrow)
            .Throws<InvalidOperationException>()
            .WithMessage("A church tenant is required for this query.");
    }

    private static ApplicationDbContext CreateContext() =>
        new DesignTimeAppDbContextFactory().CreateDbContext(
        [
            $"--ConnectionStrings:{ConnectionStrings.DatabaseKey}=Server=(localdb)\\mssqllocaldb;Database=SheafDesignTimeModelTests;Integrated Security=True;TrustServerCertificate=True"
        ]);

    private sealed class TenantProbeDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<TenantProbe>();
            base.OnModelCreating(builder);
        }
    }

    private sealed class TenantProbe : ITenanted
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }
    }
}
