using Npgsql;
using PilotFlow.Infrastructure.Persistence;

var connectionString = Environment.GetEnvironmentVariable("PILOTFLOW_MIGRATION_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("PILOTFLOW_MIGRATION_CONNECTION_STRING is required.");
    return 2;
}

try
{
    await using var dataSource = NpgsqlDataSource.Create(connectionString);
    await PostgresMigrator.ApplyAsync(dataSource, CancellationToken.None);
    Console.WriteLine("PostgreSQL migrations are up to date.");
    return 0;
}
catch (InvalidDataException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
catch (PostgresException exception)
{
    Console.Error.WriteLine($"PostgreSQL rejected the migration (SQLSTATE {exception.SqlState}).");
    return 1;
}
catch (NpgsqlException)
{
    Console.Error.WriteLine("Could not communicate with PostgreSQL.");
    return 1;
}
catch (ArgumentException)
{
    Console.Error.WriteLine("Invalid PostgreSQL connection settings.");
    return 2;
}
