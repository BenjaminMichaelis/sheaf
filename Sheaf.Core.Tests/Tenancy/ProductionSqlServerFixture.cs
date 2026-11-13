using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Respawn;
using Respawn.Graph;

using Sheaf.Data;

using TUnit.Core.Interfaces;

namespace Sheaf.Core.Tests;

/// <summary>An isolated production catalog per fixture, borrowing the assembly's SQL Server container.</summary>
public sealed class ProductionSqlServerFixture : IAsyncInitializer, IDisposable
{
    private IHost? _host;
    private string? _connectionString;
    private Respawner? _respawner;

    [ClassDataSource<SqlServerFixture>(Shared = SharedType.PerAssembly)]
    public required SqlServerFixture Sql { get; init; }

    public Task InitializeAsync()
    {
        _connectionString = new SqlConnectionStringBuilder(Sql.ConnectionString)
        {
            InitialCatalog = $"SheafProductionMigrations_{Guid.NewGuid():N}",
        }.ConnectionString;
        _host = ProductionDatabaseHost.Create(_connectionString);
        return Task.CompletedTask;
    }

    public AsyncServiceScope CreateScope(Guid? churchId = null)
    {
        IHost host = _host ?? throw new InvalidOperationException("The fixture is not initialized.");
        AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().ChurchId = churchId;
        return scope;
    }

    public async Task ResetAsync()
    {
        string connectionString = _connectionString
            ?? throw new InvalidOperationException("The fixture is not initialized.");
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        _respawner ??= await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            // Only migration history survives; Churches, Identity and all test seed rows are disposable.
            TablesToIgnore = [new Table("dbo", "__EFMigrationsHistory")],
        });
        await _respawner.ResetAsync(connection);
    }

    // The shared fixture owns the container and its catalogs; never dispose it or delete migration history here.
    public void Dispose() => _host?.Dispose();
}
