using System.Net;
using System.Net.Sockets;
using Testcontainers.PostgreSql;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// A second PostgreSQL server that a test may stop and restart (V5, #188). Bound to a fixed host
/// port chosen when the fixture starts, so the connection string a store was configured with stays
/// valid across the restart.
/// </summary>
public sealed class StoppablePostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException(
            "The second PostgreSQL container is not running. Tests using it must be [DockerRequiredFact].");

    public Task StopAsync() => _container!.StopAsync();

    public Task StartAsync() => _container!.StartAsync();

    public async Task InitializeAsync()
    {
        if (!DockerEndpoint.ShouldRun)
        {
            return;
        }

        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("microkit_messaging_stoppable")
            .WithPortBinding(FreePort(), PostgreSqlBuilder.PostgreSqlPort)
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

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
