using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Infrastructure.Persistence;

namespace Vizfolio.Api.Tests.Extracts;

internal sealed class TestDbContext : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private TestDbContext(SqliteConnection connection, AppDbContext db)
    {
        _connection = connection;
        Db = db;
    }

    public AppDbContext Db { get; }

    public static async Task<TestDbContext> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new TestDbContext(connection, db);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
