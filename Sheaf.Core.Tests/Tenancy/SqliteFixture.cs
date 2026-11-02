using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Sheaf.Core.Tests;

/// <summary>An in-memory SQLite database that lives as long as its single open connection.</summary>
public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<TenancyTestDbContext> _options;

    public SqliteFixture()
    {
        _connection.Open();
        _options = TenancyTestDbContext.BuildOptions(builder => builder.UseSqlite(_connection));
        using TenancyTestDbContext db = CreateContext(null);
        db.Database.EnsureCreated();
    }

    public TenancyTestDbContext CreateContext(Guid? churchId) => new(_options, churchId);

    public void Dispose() => _connection.Dispose();
}
