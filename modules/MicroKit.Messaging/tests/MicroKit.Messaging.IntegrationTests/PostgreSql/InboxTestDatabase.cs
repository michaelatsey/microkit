namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// A PostgreSQL database unique to one test, so tests never see each other's rows.
/// </summary>
/// <remarks>
/// Shared by the inbox suites. The outbox suite carries its own private copy; the duplication is
/// deliberate and temporary, like everything else this lot kept inbox-local, and both collapse
/// into one helper when the two sides are stable.
/// </remarks>
internal sealed class InboxTestDatabase(
    string connectionString, string adminConnectionString, string name) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public static TestMessagingDbContext NewContext(string connectionString)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    public static async Task<InboxTestDatabase> CreateAsync(string fixtureConnectionString)
    {
        var name = $"inbox_{Guid.NewGuid():N}";
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(fixtureConnectionString);
        var adminConnectionString = builder.ConnectionString;
        builder.Database = name;

        await using (var admin = new Npgsql.NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\"";
            await create.ExecuteNonQueryAsync();
        }

        var database = new InboxTestDatabase(builder.ConnectionString, adminConnectionString, name);

        await using var context = NewContext(database.ConnectionString);
        await context.Database.EnsureCreatedAsync();

        return database;
    }

    public async ValueTask DisposeAsync()
    {
        Npgsql.NpgsqlConnection.ClearAllPools();

        await using var admin = new Npgsql.NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)";
        await drop.ExecuteNonQueryAsync();
    }
}
