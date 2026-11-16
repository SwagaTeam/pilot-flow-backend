using Npgsql;
using PilotFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace PilotFlow.IntegrationTests;

public sealed class PostgresSchemaTests(PostgresSchemaFixture fixture) : IClassFixture<PostgresSchemaFixture>
{
    private const string Seed = """
        INSERT INTO pilot.analysis (id, owner_subject)
        VALUES (md5('analysis')::uuid, 'owner'), (md5('other-analysis')::uuid, 'other');
        INSERT INTO pilot.analysis_version (id, analysis_id, version_number, rules_version, title)
        VALUES (md5('version')::uuid, md5('analysis')::uuid, 1, 'fck-2026-tz-v1', 'Analysis'),
               (md5('other-version')::uuid, md5('other-analysis')::uuid, 1, 'fck-2026-tz-v1', 'Other');
        INSERT INTO pilot.product
          (id, version_id, name, unit_code, revenue, financial_evidence, duration_evidence, plan_evidence)
        VALUES (md5('product')::uuid, md5('version')::uuid, 'Product', 'item', 25, '{}', '{}', '{}'),
               (md5('other-product')::uuid, md5('other-version')::uuid, 'Other Product', 'item', 15, '{}', '{}', '{}');
        INSERT INTO pilot.operation (id, version_id, name, kind, aggregation_evidence)
        VALUES (md5('operation')::uuid, md5('version')::uuid, 'Operation', 'operation', '{}');
        INSERT INTO pilot.family (id, version_id, name, confirmation_evidence)
        VALUES (md5('family')::uuid, md5('version')::uuid, 'Family', '{}'),
               (md5('second-family')::uuid, md5('version')::uuid, 'Another family', '{}');
        INSERT INTO pilot.family_product (version_id, product_id, family_id)
        VALUES (md5('version')::uuid, md5('product')::uuid, md5('family')::uuid);
        INSERT INTO pilot.product_route_step (occurrence_id, version_id, product_id, operation_id, position, duration_hours, evidence)
        VALUES (md5('step')::uuid, md5('version')::uuid, md5('product')::uuid, md5('operation')::uuid, 1, 2, '{}');
        """;

    private const string NewDraft = """
        INSERT INTO pilot.analysis_version (id, analysis_id, version_number, rules_version, title)
        VALUES (md5('next-version')::uuid, md5('analysis')::uuid, 2, 'fck-2026-tz-v1', 'Next draft')
        """;

    private const string Fix = """
        INSERT INTO pilot.decision (
          id, version_id, analysis_id, family_id, created_by_subject, created_by_name,
          snapshot_schema_version, rules_version, snapshot, snapshot_sha256)
        VALUES (md5('decision')::uuid, md5('version')::uuid, md5('analysis')::uuid, md5('family')::uuid,
          'owner', 'Owner', '1.0', 'fck-2026-tz-v1', '{}', repeat('a', 64));
        UPDATE pilot.analysis_version SET state = 'fixed', revision = 2 WHERE id = md5('version')::uuid;
        SET CONSTRAINTS ALL IMMEDIATE;
        """;

    [Theory]
    [InlineData("one draft per analysis", NewDraft, PostgresErrorCodes.UniqueViolation)]
    [InlineData("cross-version membership",
        "INSERT INTO pilot.family_product VALUES (md5('version')::uuid, md5('other-product')::uuid, md5('family')::uuid)",
        PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("one family per product",
        "INSERT INTO pilot.family_product VALUES (md5('version')::uuid, md5('product')::uuid, md5('second-family')::uuid)",
        PostgresErrorCodes.UniqueViolation)]
    [InlineData("unique route position", """
        INSERT INTO pilot.product_route_step
            SELECT md5('duplicate-step')::uuid, version_id, product_id, operation_id, position, duration_hours, workplace_name, evidence
            FROM pilot.product_route_step;
        SET CONSTRAINTS ALL IMMEDIATE
        """, PostgresErrorCodes.UniqueViolation)]
    [InlineData("cannot delete referenced operation", "DELETE FROM pilot.operation", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("nonnegative route duration", "UPDATE pilot.product_route_step SET duration_hours = -1", PostgresErrorCodes.CheckViolation)]
    [InlineData("revision increases exactly once", "UPDATE pilot.analysis_version SET title = 'Updated'", PostgresErrorCodes.CheckViolation)]
    [InlineData("fixed version requires a decision", """
        UPDATE pilot.analysis_version SET state = 'fixed', revision = 2 WHERE id = md5('version')::uuid;
        SET CONSTRAINTS ALL IMMEDIATE
        """, PostgresErrorCodes.CheckViolation)]
    [InlineData("rules are immutable", "UPDATE pilot.rule_set SET status = 'approved'", PostgresErrorCodes.CheckViolation)]
    public async Task DraftSchema_InvariantViolation_IsRejected(string invariant, string sql, string expectedSqlState)
    {
        await using var connection = await fixture.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(transaction, Seed);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(transaction, sql));

        Assert.True(exception.SqlState == expectedSqlState, $"{invariant}: {exception.SqlState} != {expectedSqlState}.");
    }

    [Theory]
    [InlineData("UPDATE pilot.product SET name = 'Changed' WHERE id = md5('product')::uuid")]
    [InlineData("DELETE FROM pilot.product_route_step")]
    [InlineData("UPDATE pilot.analysis_version SET state = 'draft', revision = 3 WHERE id = md5('version')::uuid")]
    [InlineData("DELETE FROM pilot.decision")]
    public async Task FixedSchema_ImmutableDataCannotBeChanged(string sql)
    {
        await using var connection = await fixture.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(transaction, Seed);
        await ExecuteAsync(transaction, Fix);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(transaction, sql));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task FinancialView_WhenCostIsUnknown_PreservesNull()
    {
        await using var connection = await fixture.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(transaction, Seed);
        await using var command = new NpgsqlCommand("""
            SELECT revenue, raw_material_cost, margin_income FROM pilot.family_financials WHERE family_id = md5('family')::uuid
            """, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(25m, reader.GetDecimal(0));
        Assert.True(await reader.IsDBNullAsync(1));
        Assert.True(await reader.IsDBNullAsync(2));
    }

    [Fact]
    public async Task ProductRoute_RepeatedOperationAtAnotherPosition_IsAllowed()
    {
        await using var connection = await fixture.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(transaction, Seed);

        var inserted = await ExecuteAsync(transaction, """
            INSERT INTO pilot.product_route_step
                SELECT md5('repeat-step')::uuid, version_id, product_id, operation_id, 2, duration_hours, workplace_name, evidence
                FROM pilot.product_route_step
            """);
        await ExecuteAsync(transaction, "SET CONSTRAINTS ALL IMMEDIATE");

        Assert.Equal(1, inserted);
    }

    [Fact]
    public async Task RevisionPredicate_WhenStale_UpdatesNoRows()
    {
        await using var connection = await fixture.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(transaction, Seed);

        var changed = await ExecuteAsync(transaction, """
            UPDATE pilot.analysis_version SET revision = revision + 1 WHERE id = md5('version')::uuid AND revision = 2
            """);

        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Draft_AfterDecisionAndFixation_CanBeCreated()
    {
        await using var connection = await fixture.DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(transaction, Seed);
        await ExecuteAsync(transaction, Fix);

        Assert.Equal(1, await ExecuteAsync(transaction, NewDraft));
    }

    [Fact]
    public async Task DecisionAndVersionFixation_CommitTogether()
    {
        // Use a separate database because this test commits its rows.
        var database = new PostgreSqlBuilder("postgres:18.7-alpine")
            .WithPassword(Guid.NewGuid().ToString("N"))
            .Build();
        await using (database)
        {
            await database.StartAsync();
            await using var dataSource = NpgsqlDataSource.Create(database.GetConnectionString());
            await PostgresMigrator.ApplyAsync(dataSource, CancellationToken.None);
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await ExecuteAsync(transaction, Seed);
            await ExecuteAsync(transaction, Fix);
            await transaction.CommitAsync();

            await using var command = dataSource.CreateCommand("""
                SELECT count(*) FROM pilot.decision JOIN pilot.analysis_version ON version_id = pilot.analysis_version.id
                WHERE state = 'fixed' AND revision = 2
                """);
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
    }

    private static async Task<int> ExecuteAsync(NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, transaction.Connection, transaction);
        return await command.ExecuteNonQueryAsync();
    }
}

public sealed class PostgresSchemaFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.7-alpine")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;
    public string ConnectionString => postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        DataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        await PostgresMigrator.ApplyAsync(DataSource, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null)
        {
            await DataSource.DisposeAsync();
        }

        await postgres.DisposeAsync();
    }
}
