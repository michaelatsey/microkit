using MicroKit.Persistence.Testing;
using MicroKit.Persistence.Testing.SqlServer;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.SqlServer;

/// <summary>
/// The xUnit adapter over the published <see cref="SqlServerFixture"/>: one SQL Server container, started
/// once and shared by every test in the <see cref="SqlServerSuite"/>. Each test creates and drops its own
/// database on the server.
/// </summary>
/// <remarks>
/// The counterpart of <c>PostgreSqlSuiteFixture</c>; without a Docker daemon it starts nothing and the
/// tests skip cleanly.
/// </remarks>
public sealed class SqlServerSuiteFixture : IAsyncLifetime
{
    /// <summary>Gets the published fixture this adapter drives.</summary>
    public SqlServerFixture Server { get; } = new();

    /// <summary>Gets the connection string of the server's <c>master</c> database.</summary>
    public string ConnectionString => Server.ConnectionString;

    /// <summary>Gets a connection string to <paramref name="database"/> on the shared server.</summary>
    public string GetConnectionString(string database) => Server.GetConnectionString(database);

    public async Task InitializeAsync()
    {
        if (DockerProbe.IsAvailable)
            await Server.StartAsync();
    }

    public async Task DisposeAsync() => await Server.DisposeAsync();
}

/// <summary>
/// Groups the SQL Server tests so they share one container and run serially among themselves.
/// </summary>
/// <remarks>Named <c>…Suite</c>, not <c>…Collection</c>: CA1711 reserves that suffix.</remarks>
[CollectionDefinition(Name)]
public sealed class SqlServerSuite : ICollectionFixture<SqlServerSuiteFixture>
{
    /// <summary>The collection name shared by every SQL Server test.</summary>
    public const string Name = "SqlServer";
}
