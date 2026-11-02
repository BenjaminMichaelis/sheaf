using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Sheaf.Data.Tenancy;

public sealed class MultiTenancyExtension : IDbContextOptionsExtension
{
    public DbContextOptionsExtensionInfo Info { get; }

    public MultiTenancyExtension() => Info = new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services) =>
        services.AddScoped<IConventionSetPlugin, MultiTenancyConventionSetPlugin>();

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "using MultiTenancy ";
        public override int GetServiceProviderHashCode() => 0;
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["MultiTenancy"] = "1";
    }

    private sealed class MultiTenancyConventionSetPlugin(ICurrentDbContext currentContext) : IConventionSetPlugin
    {
        public ConventionSet ModifyConventions(ConventionSet conventionSet)
        {
            conventionSet.ModelFinalizingConventions.Add(new MultiTenancyConvention(currentContext.Context.GetType()));
            return conventionSet;
        }
    }
}
