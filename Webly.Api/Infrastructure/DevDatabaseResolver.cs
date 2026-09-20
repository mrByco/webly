using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Webly.Data;

namespace Webly.Api.Infrastructure;

/// <summary>
/// Dev-time hybrid DB strategy: prefer the persistent docker-compose Postgres (docker-compose.dev.yml);
/// if it's not reachable, fall back to an ephemeral Testcontainers Postgres so the stack still boots
/// with zero manual setup. Production always uses the configured connection string as-is.
/// </summary>
public static class DevDatabaseResolver
{
    public const string PersistentSource = "docker-compose";
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

        await container.StartAsync(cancellationToken);

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
