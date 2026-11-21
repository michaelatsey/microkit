namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// A fresh PostgreSQL database for one store, its schema created from the store's own context —
/// the way the existing suites create theirs (hosts own their migrations, #188 D14).
/// </summary>
internal sealed class StoreDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _name;

    private StoreDatabase(string connectionString, string adminConnectionString, string name)
    {
        ConnectionString = connectionString;
        _adminConnectionString = adminConnectionString;
        _name = name;
    }

    public string ConnectionString { get; }

    public static async Task<StoreDatabase> CreateAsync<TContext>(
        string serverConnectionString, Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        var name = $"store_{Guid.NewGuid():N}";
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(serverConnectionString);
        var admin = builder.ConnectionString;
        builder.Database = name;

        await using (var connection = new Npgsql.NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        var database = new StoreDatabase(builder.ConnectionString, admin, name);
        await using var context = create(new DbContextOptionsBuilder<TContext>().UseNpgsql(database.ConnectionString).Options);
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    public async ValueTask DisposeAsync()
    {
        Npgsql.NpgsqlConnection.ClearAllPools();
        try
        {
            await using var connection = new Npgsql.NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)";
            await command.ExecuteNonQueryAsync();
        }
        catch (Npgsql.NpgsqlException)
        {
            // The server is gone (a test stopped its container): nothing left to drop.
        }
    }
}
