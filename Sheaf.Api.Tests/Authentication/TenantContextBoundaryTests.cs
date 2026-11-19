using System.Reflection;

using Sheaf.Core.Authentication;
using Sheaf.Data;

namespace Sheaf.Api.Tests;

public class TenantContextBoundaryTests
{
    private static readonly Assembly[] ProductionAssemblies =
        [typeof(TenantContext).Assembly, typeof(IChurchAccessService).Assembly, typeof(Program).Assembly];

    [Test]
    public async Task TenantContext_Properties_HaveNoPublicSetters()
    {
        await Assert.That(typeof(TenantContext).GetProperties()
            .Where(property => property.SetMethod?.IsPublic == true)).IsEmpty();
    }

    [Test]
    public async Task ICurrentChurch_Properties_HaveNoSetters()
    {
        await Assert.That(typeof(ICurrentChurch).GetProperties()
            .Where(property => property.SetMethod is not null)).IsEmpty();
    }

    [Test]
    public async Task ITenantContextInitializer_ProductionImplementations_OnlyTenantContextInitializer()
    {
        await Assert.That(ProductionAssemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ITenantContextInitializer).IsAssignableFrom(type))
            .Select(type => type.FullName ?? type.Name)).IsEquivalentTo(["Sheaf.Data.TenantContext+Initializer"]);
    }
}
