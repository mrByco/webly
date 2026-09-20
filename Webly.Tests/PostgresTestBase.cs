using Webly.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Webly.Tests;

/// <summary>
/// Base for tests that need a real Postgres. One database per fixture (they are cheap; starting a container is
/// not), migrated once; each test starts from empty tables.
///
/// <b>Two ways to get that database, and the choice is the environment's.</b> By default the fixture starts a
/// Testcontainers Postgres, which needs a Docker daemon. Set <see cref="ServerVariable"/> to a connection
/// string and it instead creates its database on that server and drops it afterwards — so the suite runs on a
/// machine, a container or a CI runner that has Postgres and no Docker, which several do. It is the same
/// discipline as <c>DevDatabaseResolver</c>: prefer a server that is already there, fall back to starting one,
/// and say which happened rather than failing at the first query.
/// </summary>
public abstract class PostgresTestBase
{
    /// <summary>
    /// A connection string to an existing Postgres <i>server</i> — any database on it will do, since this only
    /// connects in order to <c>CREATE DATABASE</c>. The dev one works: the run-app stack's Postgres on 5434.
    /// </summary>
    public const string ServerVariable = "WEBLY_TEST_POSTGRES";

    private PostgreSqlContainer? _container;

    /// <summary>Set only when this fixture created its database on somebody else's server, so teardown drops it.</summary>
    private string? _ownDatabase;

    private string _connectionString = string.Empty;

    protected string ConnectionString => _connectionString;

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);

        if (string.IsNullOrWhiteSpace(server))
        {
            _container = new PostgreSqlBuilder(image: "postgres:17")
                .WithDatabase("webly_test")
                .WithUsername("webly")
                .WithPassword("webly")
                .Build();

            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }
        else
        {
            _connectionString = await CreateDatabaseAsync(server);
        }

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        if (_ownDatabase is null) return;

        // Npgsql pools per connection string, and Postgres refuses to drop a database with a live session.
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(_ownDatabase);
        await connection.OpenAsync();

        await using var drop = new NpgsqlCommand(
            $"""DROP DATABASE IF EXISTS "{DatabaseName}" WITH (FORCE)""", connection);

        await drop.ExecuteNonQueryAsync();
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

    /// <summary>
    /// One database per fixture, named after it. Not one shared database: NUnit runs fixtures sequentially
    /// today and that is exactly the kind of assumption that stops being true quietly, and a container per
    /// fixture is what this replaces — the isolation should be the same either way.
    /// </summary>
    private string DatabaseName =>
        $"webly_test_{new string([.. GetType().Name.ToLowerInvariant().Where(char.IsLetterOrDigit)])}";

    private async Task<string> CreateDatabaseAsync(string server)
    {
        _ownDatabase = server;

        await using var connection = new NpgsqlConnection(server);
        await connection.OpenAsync();

        // Dropped first: a fixture that was killed mid-run leaves its database behind, and inheriting one is
        // how a test passes because of what the last run wrote.
        foreach (var sql in new[]
        {
            $"""DROP DATABASE IF EXISTS "{DatabaseName}" WITH (FORCE)""",
            $"""CREATE DATABASE "{DatabaseName}" """
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(server) { Database = DatabaseName }.ConnectionString;
    }
}
