using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sheaf.Data;

namespace Sheaf.Core.Tests;

internal sealed class TenancyArchitectureFixture : IDisposable
{
    private readonly IHost _host = ProductionDatabaseHost.Create();
    private readonly IServiceScope _scope;

    public TenancyArchitectureFixture()
    {
        _scope = _host.Services.CreateScope();
        RuntimeContext = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    public ApplicationDbContext RuntimeContext { get; }

    public void Dispose()
    {
        _scope.Dispose();
        _host.Dispose();
    }
}
