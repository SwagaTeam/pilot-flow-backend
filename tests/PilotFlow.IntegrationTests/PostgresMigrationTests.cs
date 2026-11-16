using Npgsql;
using PilotFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace PilotFlow.IntegrationTests;

public sealed class PostgresMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.7-alpine")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ConcurrentRunners_ApplyInitialSchemaExactlyOnce()
    {
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());

        await Task.WhenAll(
            PostgresMigrator.ApplyAsync(dataSource, CancellationToken.None),
            PostgresMigrator.ApplyAsync(dataSource, CancellationToken.None));

        Assert.Equal(13L, await ScalarAsync(dataSource, "SELECT count(*) FROM pg_tables WHERE schemaname = 'pilot'"));
        Assert.Equal(2L, await ScalarAsync(dataSource, "SELECT count(*) FROM pilot_migrations.history"));
        Assert.Equal(1L, await ScalarAsync(
            dataSource,
            "SELECT count(*) FROM pilot_migrations.history WHERE version = 1 AND length(sha256) = 64 AND applied_at IS NOT NULL"));
    }

    [Fact]
    public async Task AppliedMigration_WithDifferentChecksum_IsRejected()
    {
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        await PostgresMigrator.ApplyAsync(dataSource, CancellationToken.None);
        await using var tamper = dataSource.CreateCommand(
            "UPDATE pilot_migrations.history SET sha256 = repeat('0', 64) WHERE version = 1");
        await tamper.ExecuteNonQueryAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => PostgresMigrator.ApplyAsync(dataSource, CancellationToken.None));

        Assert.Contains("checksum", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedMigration_RollsBackHistory()
    {
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        await using var conflict = dataSource.CreateCommand("CREATE SCHEMA pilot");
        await conflict.ExecuteNonQueryAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresMigrator.ApplyAsync(dataSource, CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.DuplicateSchema, error.SqlState);
        Assert.Equal(0L, await ScalarAsync(
            dataSource,
            "SELECT count(*) FROM pg_namespace WHERE nspname = 'pilot_migrations'"));
    }

    [Fact]
    public async Task QualifiedQueries_UsePermanentTablesForTriggers()
    {
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        await PostgresMigrator.ApplyAsync(dataSource, CancellationToken.None);
        await using var command = dataSource.CreateCommand("""
            CREATE TEMP TABLE analysis_version (LIKE pilot.analysis_version);
            INSERT INTO pilot.analysis (id, owner_subject) VALUES (gen_random_uuid(), 'operator');
            INSERT INTO pilot.analysis_version (id, analysis_id, version_number, rules_version, title)
                SELECT gen_random_uuid(), id, 1, 'fck-2026-tz-v1', 'Test' FROM pilot.analysis;
            INSERT INTO pilot.protocol (version_id) SELECT id FROM pilot.analysis_version;
            """);
        await command.ExecuteNonQueryAsync(CancellationToken.None);

        Assert.Equal(1L, await ScalarAsync(dataSource, "SELECT count(*) FROM pilot.protocol"));
    }

    private static async Task<long> ScalarAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        return (long)(await command.ExecuteScalarAsync(CancellationToken.None))!;
    }
}
