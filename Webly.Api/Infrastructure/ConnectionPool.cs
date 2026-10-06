using System.Data.Common;
using Npgsql;

namespace Webly.Api.Infrastructure;

/// <summary>
/// Keeps the connection pool below what the database server will accept, unless the connection string chooses a
/// size itself.
///
/// Npgsql's default pool is a hundred connections and so is Postgres's default <c>max_connections</c>, which leaves
/// the server nothing for anybody else and puts a burst's failure in the wrong place: the request that wants the
/// hundred-and-first connection is refused by the server — "sorry, too many clients already", answered as a 500 —
/// instead of waiting a few milliseconds in the pool for one of those already open. Found by sending three hundred
/// requests at once at the running app; two of them answered 500, every time. Fifty leaves the server room for a
/// migration, a psql session, a backup and the next container during a redeploy. Two instances of the API would
/// take a hundred between them again, which is one more thing on the list in CLAUDE.md about scaling out.
/// </summary>
public static class ConnectionPool
{
    public const int DefaultMaxSize = 50;

    public static string Capped(string connectionString)
    {
        // Asked of the plain parser rather than Npgsql's, which reports its default for a key nobody wrote and so
        // cannot tell "said a hundred" from "said nothing".
        var given = new DbConnectionStringBuilder { ConnectionString = connectionString };

        if (given.ContainsKey("Maximum Pool Size") || given.ContainsKey("MaxPoolSize"))
            return connectionString;

        return new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = DefaultMaxSize }.ConnectionString;
    }
}
