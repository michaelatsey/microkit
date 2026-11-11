namespace MicroKit.Messaging.Transport;

using MicroKit.Messaging.Bus;
using MicroKit.Messaging.Publishing;
using Microsoft.Extensions.Options;

/// <summary>
/// Binds this service's endpoint on the <see cref="InMemoryBroker"/>, once: the service name to
/// the wire types its handlers handle.
/// </summary>
/// <remarks>
/// <para>
/// <b>On first transport use or at host start, whichever comes first.</b>
/// <see cref="InMemoryTransport"/>'s constructor calls <see cref="EnsureBound"/>, and so does
/// <see cref="InMemoryTransportWorker"/>'s <c>StartingAsync</c>. A message published before the host
/// starts — seeding before <c>RunAsync</c>, an outbox row staged by an earlier run, a hosted
/// service publishing in its own <c>StartAsync</c> — therefore already finds the endpoint bound and
/// is held there until the consume loop starts, as the single queue of #186 held it.
/// </para>
/// <para>
/// <b>The composition is validated before anything is bound</b>, through the same checks as
/// <see cref="MessagingCompositionValidator"/>: an invalid composition binds nothing, and the call
/// throws the same <see cref="IntegrationEventConfigurationException"/>. A failed call binds nothing
/// and is retried by the next one.
/// </para>
/// <para>
/// It takes the root <see cref="IServiceProvider"/> and resolves the registry and the options
/// inside <see cref="EnsureBound"/>, not through its constructor: the host resolves every hosted
/// service before it starts any, and building the registry there would throw on an invalid
/// composition before the validator could list every problem.
/// </para>
/// </remarks>
internal sealed class InMemoryEndpointBinder(
    InMemoryBroker broker,
    IServiceProvider services,
    ILogger<InMemoryEndpointBinder> logger)
{
    private readonly Lock _gate = new();
    private string? _endpoint;

    /// <summary>Validates the composition and binds the endpoint, the first time it is called.</summary>
    /// <returns>The endpoint: this service's name.</returns>
    /// <exception cref="IntegrationEventConfigurationException">The composition is invalid.</exception>
    public string EnsureBound()
    {
        if (Volatile.Read(ref _endpoint) is { } bound)
        {
            return bound;
        }

        lock (_gate)
        {
            if (_endpoint is { } raced)
            {
                return raced;
            }

            // Validation only: the start-up warnings are the hosted validator's to log, once.
            new MessagingCompositionValidator(
                services.GetRequiredService<MessagingComposition>(),
                services.GetRequiredService<IServiceProviderIsService>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MessagingCompositionValidator>.Instance).Validate();

            var registry = services.GetRequiredService<IntegrationEventRegistry>();
            var endpoint = services.GetRequiredService<IOptions<MessagingOptions>>().Value.ServiceName!;
            var wireTypes = registry.SubscribedContractNames;

            broker.Bind(endpoint, wireTypes);
            BusLogs.EndpointBound(logger, endpoint, wireTypes.Count, string.Join(", ", wireTypes));

            Volatile.Write(ref _endpoint, endpoint);
            return endpoint;
        }
    }
}
