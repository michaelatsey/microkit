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

        // The port is probed free here, but Docker publishes it on its own host — under Docker
        // Desktop on WSL, Windows — which may still refuse it (a reserved range, a late taker).
        // A refused port is retried with another; any other failure is the test's.
        for (var attempt = 1; ; attempt++)
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("microkit_messaging_stoppable")
                .WithPortBinding(FreePort(), PostgreSqlBuilder.PostgreSqlPort)
                .Build();
            try
            {
                await _container.StartAsync();
                return;
            }
            catch (Docker.DotNet.DockerApiException ex)
                when (attempt < 5 && ex.Message.Contains("ports are not available", StringComparison.Ordinal))
            {
                await _container.DisposeAsync();
            }
        }
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
