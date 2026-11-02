using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Sheaf.Data.Tenancy;

public static class MultiTenancyDbContextOptionsBuilderExtensions
{
    /// <summary>Enables church multi-tenancy; the context must implement <see cref="ITenantContext"/>.</summary>
    public static DbContextOptionsBuilder<TContext> UseMultiTenancy<TContext>(this DbContextOptionsBuilder<TContext> builder)
        where TContext : DbContext, ITenantContext
    {
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new MultiTenancyExtension());
        return builder;
    }
}
