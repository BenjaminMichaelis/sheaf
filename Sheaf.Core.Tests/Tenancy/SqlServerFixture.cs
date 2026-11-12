using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Respawn;
using Respawn.Graph;

using Testcontainers.MsSql;

using TUnit.Core.Interfaces;

namespace Sheaf.Core.Tests;

/// <summary>One SQL Server container for the whole test assembly; <see cref="ResetAsync"/> empties it between tests.</summary>
public sealed class SqlServerFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string Image =
        "mcr.microsoft.com/mssql/server:2025-CU9-GDR1-ubuntu-24.04";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();
    private DbContextOptions<TenancyTestDbContext>? _options;
    private Respawner? _respawner;
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "SheafTenancyTests",
        }.ConnectionString;
        _options = TenancyTestDbContext.BuildOptions(builder => builder.UseAzureSql(_connectionString));

        await using TenancyTestDbContext db = CreateContext(null);
        await db.Database.MigrateAsync();

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            TablesToIgnore = [new Table("dbo", "__EFMigrationsHistory")],
        });
    }

    public TenancyTestDbContext CreateContext(Guid? churchId) =>
        new(_options ?? throw new InvalidOperationException("The fixture is not initialized."), churchId);

    public async Task ResetAsync()
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await (_respawner ?? throw new InvalidOperationException("The fixture is not initialized.")).ResetAsync(connection);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
