using Testcontainers.MsSql;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.SqlServer;

/// <summary>
/// One SQL Server container, started once and shared by every test in the
/// <see cref="SqlServerSuite"/>.
/// </summary>
/// <remarks>
/// The counterpart of <c>PostgreSqlFixture</c>: shared so the image is pulled and the server
/// started once, and so the tests that need it run serially among themselves. Each test creates
/// and drops its own database on the server.
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    /// <summary>Gets the connection string for the running container (database <c>master</c>).</summary>
    public string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException(
            "The SQL Server container is not running. Tests using it must be marked " +
            "[DockerRequiredFact] so they are skipped when Docker is absent.");

    public async Task InitializeAsync()
    {
        // Constructed even when every test in the collection is skipped: without a daemon, skip
        // cleanly instead of failing at fixture setup.
        if (!DockerEndpoint.IsAvailable)
            return;

        // The image goes through the constructor: the parameterless MsSqlBuilder() is obsolete.
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

/// <summary>
/// Groups the SQL Server tests so they share one container and run serially among themselves.
/// </summary>
/// <remarks>Named <c>…Suite</c>, not <c>…Collection</c>: CA1711 reserves that suffix.</remarks>
[CollectionDefinition(Name)]
public sealed class SqlServerSuite : ICollectionFixture<SqlServerFixture>
{
    /// <summary>The collection name shared by every SQL Server test.</summary>
    public const string Name = "SqlServer";
}
