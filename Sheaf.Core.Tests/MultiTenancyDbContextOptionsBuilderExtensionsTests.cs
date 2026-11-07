using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

using Sheaf.Data;
using Sheaf.Data.Tenancy;

namespace Sheaf.Core.Tests;

public class MultiTenancyDbContextOptionsBuilderExtensionsTests
{
    [Test]
    public async Task UseMultiTenancy_NonGenericBuilderWithTenantContract_AddsTenancyExtension()
    {
        DbContextOptionsBuilder builder = new DbContextOptionsBuilder<TenantedContext>();

        builder.UseMultiTenancy();

        await Assert.That(builder.Options.FindExtension<MultiTenancyExtension>()).IsNotNull();
    }

    [Test]
    public async Task UseMultiTenancy_ContextWithoutTenantContract_Throws()
    {
        DbContextOptionsBuilder builder = new();

        await Assert.That(() => builder.UseMultiTenancy())
            .Throws<InvalidOperationException>()
            .WithMessageContaining(typeof(ITenantContext).FullName ?? nameof(ITenantContext));
    }

    [Test]
    public async Task UseMultiTenancy_RequestStateWithoutDatabaseContract_Throws()
    {
        DbContextOptionsBuilder builder = new DbContextOptionsBuilder<RequestOnlyContext>();

        await Assert.That(() => builder.UseMultiTenancy())
            .Throws<InvalidOperationException>()
            .WithMessageContaining(typeof(ITenantContext).FullName ?? nameof(ITenantContext));
    }

    [Test]
    public async Task UseMultiTenancy_RepeatedGenericAndNonGenericCalls_RegistersOneTenancyInterceptor()
    {
        DbContextOptionsBuilder<TenantedContext> builder = new DbContextOptionsBuilder<TenantedContext>()
            .UseInMemoryDatabase(nameof(UseMultiTenancy_RepeatedGenericAndNonGenericCalls_RegistersOneTenancyInterceptor))
            .UseMultiTenancy();
        ((DbContextOptionsBuilder)builder).UseMultiTenancy();
        builder.UseMultiTenancy();
        await using TenantedContext context = new(builder.Options);

        IDbContextOptions configuredOptions = context.GetService<IDbContextOptions>();
        await Assert.That(configuredOptions.FindExtension<MultiTenancyExtension>()).IsNotNull();
        IEnumerable<TenancySaveChangesInterceptor> interceptors =
            configuredOptions.FindExtension<CoreOptionsExtension>()?.Interceptors?
                .OfType<TenancySaveChangesInterceptor>() ?? [];
        await Assert.That(interceptors).Count().IsEqualTo(1);
    }

    private sealed class TenantedContext(DbContextOptions<TenantedContext> options)
        : DbContext(options), ITenantContext
    {
        public Guid ChurchId => Guid.Empty;
    }

    private sealed class RequestOnlyContext : DbContext, ICurrentChurch
    {
        public Guid? ChurchId => null;
        public Guid? LeaderId => null;
    }
}
