using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace PilotFlow.Infrastructure.Persistence;

public static class PostgresMigrator
{
    private const long MigrationLockKey = 0x50494C4F54464C4F;
    private const string ResourcePrefix = "Migrations.V";
    private const string ResourceSuffix = ".sql";

    public static async Task ApplyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var migrations = ReadMigrations();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using var acquireLock = new NpgsqlCommand("SELECT pg_catalog.pg_advisory_xact_lock($1)", connection, transaction);
        acquireLock.Parameters.Add(new NpgsqlParameter<long> { TypedValue = MigrationLockKey });
        await acquireLock.ExecuteNonQueryAsync(cancellationToken);

        await using var initialize = new NpgsqlCommand("""
            CREATE SCHEMA IF NOT EXISTS pilot_migrations;
            CREATE TABLE IF NOT EXISTS pilot_migrations.history (
                version integer PRIMARY KEY,
                sha256 text NOT NULL CHECK (length(sha256) = 64),
                applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
            );
            """, connection, transaction);
        await initialize.ExecuteNonQueryAsync(cancellationToken);

        var appliedCount = await VerifyHistoryAsync(connection, transaction, migrations, cancellationToken);
        foreach (var migration in migrations.Skip(appliedCount))
        {
            // Reset the path even when an earlier migration changed it in this transaction.
            await using var run = new NpgsqlCommand(
                "SET LOCAL search_path = pilot, pg_temp;\n" + migration.Sql, connection, transaction);
            await run.ExecuteNonQueryAsync(cancellationToken);

            await using var record = new NpgsqlCommand(
                "INSERT INTO pilot_migrations.history (version, sha256) VALUES ($1, $2)", connection, transaction);
            record.Parameters.Add(new NpgsqlParameter<int> { TypedValue = migration.Version });
            record.Parameters.Add(new NpgsqlParameter<string> { TypedValue = migration.Sha256 });
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<int> VerifyHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Migration[] migrations,
        CancellationToken cancellationToken)
    {
        await using var history = new NpgsqlCommand(
            "SELECT version, sha256 FROM pilot_migrations.history ORDER BY version", connection, transaction);
        await using var rows = await history.ExecuteReaderAsync(cancellationToken);
        var i = 0;
        while (await rows.ReadAsync(cancellationToken))
        {
            var version = rows.GetInt32(0);
            if (i >= migrations.Length || migrations[i].Version != version)
            {
                throw new InvalidDataException($"Database migration {version} does not match the packaged sequence.");
            }

            if (!string.Equals(rows.GetString(1), migrations[i].Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Database migration {version} has a different SHA-256 checksum.");
            }

            i++;
        }

        return i;
    }

    private static Migration[] ReadMigrations()
    {
        var assembly = typeof(PostgresMigrator).Assembly;
        var migrations = assembly.GetManifestResourceNames()
            .Where(static name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Select(name =>
            {
                var version = int.Parse(
                    name.AsSpan(ResourcePrefix.Length, name.Length - ResourcePrefix.Length - ResourceSuffix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture);
                using var stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidDataException($"Migration resource {name} was not found.");
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                return new Migration(version, Encoding.UTF8.GetString(bytes), Convert.ToHexString(SHA256.HashData(bytes)));
            })
            .OrderBy(static migration => migration.Version)
            .ToArray();

        if (migrations.Length == 0 || migrations.Where(static (migration, i) => migration.Version != i + 1).Any())
        {
            throw new InvalidDataException("Packaged migrations must form a contiguous sequence starting at V001.");
        }

        return migrations;
    }

    private sealed record Migration(int Version, string Sql, string Sha256);
}
