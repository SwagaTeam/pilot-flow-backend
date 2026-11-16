using Npgsql;

namespace PilotFlow.IntegrationTests;

public sealed class AnalysisApiDatabase : IAsyncLifetime
{
    private readonly PostgresSchemaFixture postgres = new();

    public NpgsqlDataSource DataSource => postgres.DataSource;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await postgres.InitializeAsync();
        var credentials = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Username = "pilot_app",
            Password = Guid.NewGuid().ToString("N"),
        };
        await using var format = DataSource.CreateCommand("SELECT format('CREATE ROLE pilot_app LOGIN PASSWORD %L', $1)");
        format.Parameters.Add(new NpgsqlParameter<string> { TypedValue = credentials.Password });
        var createRoleSql = (string)(await format.ExecuteScalarAsync(CancellationToken.None))!;
        var grantsPath = Path.Combine(AppContext.BaseDirectory, "analysis-grants.sql");

        await using var create = DataSource.CreateCommand(createRoleSql + ";\n"
            + await File.ReadAllTextAsync(grantsPath, CancellationToken.None));
        await create.ExecuteNonQueryAsync(CancellationToken.None);
        ConnectionString = credentials.ConnectionString;
    }

    public Task DisposeAsync() => postgres.DisposeAsync();
}
