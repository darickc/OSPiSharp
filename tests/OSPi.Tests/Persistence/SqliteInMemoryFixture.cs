using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OSPi.Infrastructure.Persistence;

namespace OSPi.Tests.Persistence;

/// <summary>
/// Holds a single open in-memory SQLite connection for a test's lifetime (the DB exists only
/// while the connection is open) and creates the schema + seed data via <c>EnsureCreated</c>.
/// Real SQLite is used (not the EF in-memory provider) so FK, unique-index, and cascade
/// behavior is actually exercised. Also exposes an <see cref="IDbContextFactory{T}"/> over the
/// same connection so repositories can be tested as they run in production.
/// </summary>
public sealed class SqliteInMemoryFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OSPiDbContext> _options;

    public SqliteInMemoryFixture()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<OSPiDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new OSPiDbContext(_options);
        db.Database.EnsureCreated();
    }

    /// <summary>Creates a fresh context over the shared connection.</summary>
    public OSPiDbContext CreateContext() => new(_options);

    /// <summary>A context factory over the shared connection, for repository tests.</summary>
    public IDbContextFactory<OSPiDbContext> Factory => new SharedConnectionFactory(_options);

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private sealed class SharedConnectionFactory : IDbContextFactory<OSPiDbContext>
    {
        private readonly DbContextOptions<OSPiDbContext> _options;
        public SharedConnectionFactory(DbContextOptions<OSPiDbContext> options) => _options = options;
        public OSPiDbContext CreateDbContext() => new(_options);
    }
}
