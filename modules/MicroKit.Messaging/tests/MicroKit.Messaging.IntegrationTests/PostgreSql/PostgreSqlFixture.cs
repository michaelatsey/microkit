using Testcontainers.PostgreSql;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// One PostgreSQL container, started once and shared by every test in the
/// <see cref="PostgreSqlCollection"/>.
/// </summary>
/// <remarks>
/// Shared rather than per-class so the image is pulled and the server started once, and so the
/// Docker-backed tests run serially among themselves instead of contending for containers while
/// the fast SQLite tests carry on in their own collections.
/// </remarks>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>Gets the connection string for the running container.</summary>
    public string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException(
            "The PostgreSQL container is not running. Tests using it must be marked " +
            "[DockerRequiredFact] so they are skipped when Docker is absent.");

    public async Task InitializeAsync()
    {
        // Constructed even when every test in the collection is skipped, so the Docker check
        // has to happen here too — otherwise a machine without Docker fails at fixture setup
        // instead of skipping cleanly.
        if (!DockerEndpoint.IsAvailable)
        {
            return;
        }

        // The image goes through the constructor: the parameterless PostgreSqlBuilder() is
        // obsolete as of Testcontainers 4.x.
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("microkit_messaging_tests")
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

/// <summary>
/// Groups the Docker-backed tests so they share one container and run serially among themselves.
/// </summary>
/// <remarks>
/// Named <c>…Suite</c> rather than <c>…Collection</c>: CA1711 reserves the <c>Collection</c>
/// suffix for types that actually are collections, and this one is an xunit grouping marker.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class PostgreSqlSuite : ICollectionFixture<PostgreSqlFixture>
{
    /// <summary>The collection name shared by every Docker-backed outbox test.</summary>
    public const string Name = "PostgreSql";
}
