using Testcontainers.MsSql;

using TUnit.Core.Interfaces;

namespace Sheaf.Core.Tests;

/// <summary>One SQL Server container for the assembly; each production fixture owns its catalog.</summary>
public sealed class SqlServerFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string Image =
        "mcr.microsoft.com/mssql/server:2025-CU9-GDR1-ubuntu-24.04";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();
    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
