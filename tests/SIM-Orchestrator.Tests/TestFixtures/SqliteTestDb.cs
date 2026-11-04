using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SIMOrchestrator.Data;

namespace SIMOrchestrator.Tests.TestFixtures;

/// <summary>
/// A private in-memory SQLite database with the real migrations applied, so unique
/// indexes and SQL semantics match production. Each DbContext gets its own connection.
/// </summary>
public sealed class SqliteTestDb : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public SqliteTestDb()
    {
        ConnectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();

        using var context = CreateContext();
        context.Database.Migrate();
    }

    public string ConnectionString { get; }

    public DbContextOptions<AppDbContext> Options =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options;

    public AppDbContext CreateContext() => new(Options);

    public void Dispose() => _keepAlive.Dispose();
}
