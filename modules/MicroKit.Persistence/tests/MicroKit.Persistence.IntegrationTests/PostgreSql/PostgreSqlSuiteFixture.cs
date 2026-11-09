using MicroKit.Persistence.Testing;
using MicroKit.Persistence.Testing.PostgreSql;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.PostgreSql;

/// <summary>
/// The xUnit adapter over the published <see cref="PostgreSqlFixture"/>: one PostgreSQL container, started
/// once and shared by every test in the <see cref="PostgreSqlSuite"/>.
/// </summary>
/// <remarks>
/// <para>
/// The fixture package forces no test framework (#197 phase 7, ADR-015); this is the whole adapter a
/// consumer on xUnit writes. Shared rather than per-class, so the image is pulled and the server started
/// once, and so the Docker-backed tests run serially among themselves while the SQLite tests carry on.
/// </para>
/// <para>
/// xUnit constructs it even when every test of the collection is skipped, so it asks the probe before
/// starting: without a daemon it starts nothing and the tests skip cleanly.
/// </para>
/// </remarks>
public sealed class PostgreSqlSuiteFixture : IAsyncLifetime
{
    /// <summary>Gets the published fixture this adapter drives.</summary>
    public PostgreSqlFixture Server { get; } = new();

    /// <summary>Gets the connection string of the server's default database.</summary>
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
/// Groups the Docker-backed tests so they share one container and run serially among themselves.
/// </summary>
/// <remarks>
/// Named <c>…Suite</c> rather than <c>…Collection</c>: CA1711 reserves the <c>Collection</c>
/// suffix for types that actually are collections, and this one is an xunit grouping marker.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class PostgreSqlSuite : ICollectionFixture<PostgreSqlSuiteFixture>
{
    /// <summary>The collection name shared by every Docker-backed test.</summary>
    public const string Name = "PostgreSql";
}
