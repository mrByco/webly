using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Webly.Data;

namespace Webly.Api.Infrastructure;

/// <summary>
/// Dev-time hybrid DB strategy: prefer whatever persistent Postgres is listening on the configured port —
/// the docker-compose one in <c>docker-compose.dev.yml</c>, or a locally installed server, because the port
/// is all this can tell apart — and fall back to an ephemeral Testcontainers instance so that a machine with
/// Docker and no database still boots with no setup at all. Production always uses the configured connection
/// string as-is.
///
/// The fallback needs a Docker daemon, and plenty of development machines, CI runners and containers do not
/// have one. That case is turned into a sentence naming both remedies rather than a Testcontainers stack
/// trace, because it is the first thing somebody sees on a fresh clone and "the app will not start" is a
/// worse first impression than either fix is a chore.
/// </summary>
public static class DevDatabaseResolver
{
    /// <summary>
    /// Not "docker-compose": all this knows is that something answered on the configured port, which is just
    /// as likely to be a Postgres the developer installed themselves.
    /// </summary>
    public const string PersistentSource = "persistent";

    public const string FallbackSource = "testcontainers";

    public static async Task<(string ConnectionString, string Source)> ResolveAsync(
        string configuredConnectionString,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var failure = await TryConnectAsync(configuredConnectionString, cancellationToken);
        if (failure is null)
        {
            logger.LogInformation("Dev DB: connected to persistent Postgres ({Source}).", PersistentSource);
            return (configuredConnectionString, PersistentSource);
        }

        logger.LogWarning(
            "Dev DB: persistent Postgres not reachable ({Reason}), falling back to an ephemeral Testcontainers instance.",
            failure);

        var container = new PostgreSqlBuilder(image: "postgres:17")
            .WithDatabase("webly")
            .WithUsername("webly")
            .WithPassword("webly")
            .Build();

        try
        {
            await container.StartAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Both remedies, in the message, because this is what a fresh clone hits and the underlying error
            // ("Cannot connect to the Docker daemon") names neither of them. Data in the fallback is gone on the
            // next restart anyway, so the persistent option is the better first suggestion.
            throw new InvalidOperationException(
                $"""
                No database. The persistent Postgres was not reachable ({failure}), and the Testcontainers
                fallback could not start either ({exception.Message}).

                Either of these fixes it:
                  * docker compose -f docker-compose.dev.yml up -d
                  * start any Postgres on the port in ConnectionStrings:WeblyDb (5434 by default) with the
                    database, user and password that string names.
                """,
                exception);
        }

        logger.LogInformation("Dev DB: started Testcontainers Postgres ({Source}).", FallbackSource);
        return (container.GetConnectionString(), FallbackSource);
    }

    /// <returns>null on success, otherwise the reason the connection failed.</returns>
    private static async Task<string?> TryConnectAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 2 };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
