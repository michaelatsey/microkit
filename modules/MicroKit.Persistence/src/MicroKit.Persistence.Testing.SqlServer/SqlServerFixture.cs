using Testcontainers.MsSql;

namespace MicroKit.Persistence.Testing.SqlServer;

/// <summary>
/// One SQL Server in a Docker container, for integration tests, with an <see cref="IAsyncDisposable"/>
/// lifecycle and no test framework: start it with <see cref="StartAsync"/>, give each test its own database
/// with <see cref="GetConnectionString"/>, dispose it to remove the container.
/// </summary>
/// <remarks>
/// <para>
/// The fixture does not consult <see cref="DockerProbe"/>: ask <see cref="DockerProbe.IsAvailable"/> before
/// starting it and skip the test, in the framework's own way, when it answers <see langword="false"/>.
/// Without a daemon, <see cref="StartAsync"/> fails with Testcontainers' own exception.
/// </para>
/// <para>
/// Testcontainers' SQL Server builder sets <c>ACCEPT_EULA=Y</c> on the container: using this fixture accepts
/// the SQL Server end-user licence agreement. The default image is built for amd64 and runs emulated on ARM.
/// </para>
/// <para>
/// Wrap it in the framework's lifecycle hook: xUnit's <c>IAsyncLifetime</c> or a class or collection
/// fixture, NUnit's <c>[OneTimeSetUp]</c>/<c>[OneTimeTearDown]</c>, MSTest's <c>[ClassInitialize]</c>/
/// <c>[ClassCleanup]</c>. Share one fixture across many tests: a server takes seconds to start, a database
/// on it far less.
/// </para>
/// </remarks>
public sealed class SqlServerFixture : IAsyncDisposable
{
    private readonly ContainerLifecycle<MsSqlContainer> _lifecycle;

    /// <summary>Initializes a new instance of the <see cref="SqlServerFixture"/> class on <see cref="DefaultImage"/>.</summary>
    public SqlServerFixture()
        : this(DefaultImage)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SqlServerFixture"/> class on a given image.</summary>
    /// <param name="image">The SQL Server image, such as <c>mcr.microsoft.com/mssql/server:2022-latest</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="image"/> is null, empty or white space.</exception>
    public SqlServerFixture(string image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        _lifecycle = new ContainerLifecycle<MsSqlContainer>(
            typeof(SqlServerFixture),
            () => new MsSqlBuilder(image).Build(),
            static (container, ct) => container.StartAsync(ct));
    }

    /// <summary>Gets the image the parameterless constructor uses: <c>mcr.microsoft.com/mssql/server:2022-latest</c>.</summary>
    /// <remarks>Read at run time, so a new default reaches callers without a recompilation.</remarks>
    public static string DefaultImage { get; } = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>Gets the connection string of the running server's <c>master</c> database.</summary>
    /// <exception cref="InvalidOperationException">No start has completed successfully.</exception>
    /// <exception cref="ObjectDisposedException">The fixture is disposed.</exception>
    public string ConnectionString => _lifecycle.Running.GetConnectionString();

    /// <summary>Starts the container once; every call awaits that one start.</summary>
    /// <remarks>
    /// The first start may pull the image, which can take minutes. The start runs under no caller's token:
    /// <paramref name="ct"/> cancels only this call's wait, and the start goes on for the other callers. A
    /// start that fails is not retried: every later call rethrows its exception. Once the fixture is disposed,
    /// every call throws <see cref="ObjectDisposedException"/>; a call already waiting when
    /// <see cref="DisposeAsync"/> interrupts the start receives the start's <see cref="OperationCanceledException"/>.
    /// Docker and Testcontainers exceptions propagate unchanged. Every exception comes through the returned task.
    /// </remarks>
    /// <param name="ct">Cancels the wait of this call.</param>
    /// <returns>A task that completes when the server accepts connections.</returns>
    /// <exception cref="ObjectDisposedException">The fixture is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled, or <see cref="DisposeAsync"/>
    /// interrupted the start this call was waiting for.</exception>
    public ValueTask StartAsync(CancellationToken ct = default) => _lifecycle.StartAsync(ct);

    /// <summary>
    /// Gets a connection string to another database of the running server, typically one per test. The
    /// database is not created: <c>EnsureCreated</c> or a migration creates it.
    /// </summary>
    /// <param name="database">The database name; quoted as needed.</param>
    /// <returns><see cref="ConnectionString"/> with its <c>Database</c> replaced.</returns>
    /// <exception cref="ArgumentException"><paramref name="database"/> is null, empty or white space.</exception>
    /// <exception cref="InvalidOperationException">No start has completed successfully.</exception>
    /// <exception cref="ObjectDisposedException">The fixture is disposed.</exception>
    public string GetConnectionString(string database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        return ConnectionStrings.WithDatabase(ConnectionString, database);
    }

    /// <summary>
    /// Stops and removes the container. A running start is cancelled and awaited first. Idempotent — every
    /// call, concurrent ones included, completes once the container is removed — and a no-op on a fixture that
    /// never started. A failure to remove the container propagates.
    /// </summary>
    /// <returns>A task that completes when the container is removed.</returns>
    public ValueTask DisposeAsync() => _lifecycle.DisposeAsync();
}
