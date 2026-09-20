using Webly.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Webly.Tests;

/// <summary>
/// Base for tests that need a real Postgres. One container per fixture (starting one is the
/// expensive part), migrated once; each test starts from empty tables.
/// </summary>
public abstract class PostgresTestBase
{
    private PostgreSqlContainer _container = null!;

    protected string ConnectionString => _container.GetConnectionString();

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        _container = new PostgreSqlBuilder(image: "postgres:17")
            .WithDatabase("webly_test")
            .WithUsername("webly")
            .WithPassword("webly")
            .Build();

        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        await _container.DisposeAsync();
    }

    [SetUp]
    public async Task ClearTables()
    {
        await using var db = CreateContext();

        // Truncate rather than re-migrate: same clean slate, a fraction of the cost.
        //
        // The table list is asked for rather than written down. It used to be a literal that every
        // new domain area had to remember to extend, in dependency order — a special case added to
        // shared plumbing once per feature, and a forgotten one leaks rows between tests in a way
        // that looks like a bug in whatever ran second. CASCADE means the order never mattered.
        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            DECLARE tables text;
            BEGIN
                SELECT string_agg(format('%I.%I', schemaname, tablename), ', ')
                INTO tables
                FROM pg_tables
                WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory';

                IF tables IS NOT NULL THEN
                    EXECUTE 'TRUNCATE ' || tables || ' RESTART IDENTITY CASCADE';
                END IF;
            END $$;
            """);
    }

    protected WeblyDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WeblyDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new WeblyDbContext(options);
    }
}
