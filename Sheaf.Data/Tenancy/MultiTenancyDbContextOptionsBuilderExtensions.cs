using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Sheaf.Data.Tenancy;

public static class MultiTenancyDbContextOptionsBuilderExtensions
{
    /// <summary>Enables church multi-tenancy; the context must implement <see cref="ITenantContext"/>.</summary>
    public static DbContextOptionsBuilder<TContext> UseMultiTenancy<TContext>(this DbContextOptionsBuilder<TContext> builder)
        where TContext : DbContext, ITenantContext
    {
        ((DbContextOptionsBuilder)builder).UseMultiTenancy();
        return builder;
    }

    /// <summary>Enables church multi-tenancy after validating the configured context implements <see cref="ITenantContext"/>.</summary>
    public static DbContextOptionsBuilder UseMultiTenancy(this DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Type contextType = builder.Options.ContextType;
        if (!typeof(ITenantContext).IsAssignableFrom(contextType))
        {
            throw new InvalidOperationException(
                $"The context '{contextType.Name}' must implement {typeof(ITenantContext).FullName} to use multi-tenancy.");
        }

        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new MultiTenancyExtension());

        if (builder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors?
                .OfType<TenancySaveChangesInterceptor>().Any() != true)
        {
            builder.AddInterceptors(new TenancySaveChangesInterceptor());
        }

        return builder;
    }
}
