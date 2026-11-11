namespace MicroKit.Messaging;

using System.Collections.Concurrent;
using System.Threading.Channels;

/// <summary>
/// The broker behind the in-memory transport: publish/subscribe by endpoint, held in process memory.
/// </summary>
/// <remarks>
/// <para>
/// <b>Endpoints (#187).</b> An endpoint is a service's <c>ServiceName</c>. When a host starts, its
/// in-memory transport binds the endpoint to the wire types its handlers handle. A send copies the
/// envelope to <b>every endpoint bound to its type</b>: two services on one broker each receive
/// their own copy. Hosts sharing a <c>ServiceName</c> share one endpoint and compete for its
/// messages — scale-out — so each message is handled once between them. A type no endpoint is
/// bound to reaches no one, and the transport logs it at <c>Warning</c>; it is not an error, since
/// a publisher need not know who subscribes.
/// </para>
/// <para>
/// <b>When an endpoint is bound.</b> A host binds its own endpoint on the first use of its
/// transport or when it starts, whichever comes first, so a single host never loses a message it
/// publishes to itself before it starts: the copy waits on its endpoint for the consume loop.
/// </para>
/// <para>
/// <b>Hosts sharing one broker — tests only.</b> Start the consumers before the producers: a
/// message sent while no endpoint is yet bound to its type reaches no one (<c>Warning</c> 3014). And
/// set <c>bus.ServiceName</c> on each host: one that leaves it unset takes
/// <c>IHostEnvironment.ApplicationName</c> — the entry assembly by default — so two such hosts in
/// one process share one endpoint and compete for each other's messages.
/// </para>
/// <para>
/// <b>A host may supply its own broker</b> through <see cref="MessagingBuilder.UseInMemoryTransport"/>,
/// to connect several hosts — run one after the other, or at the same time. A message one host left
/// unsettled at shutdown is delivered by the next host on that endpoint. Bindings accumulate for the
/// broker's lifetime: an endpoint keeps receiving what it was bound to while no host consumes it.
/// Nothing survives the process.
/// </para>
/// <para>
/// <b>Redelivery is bounded, per endpoint.</b> A transient failure counts an attempt and
/// redelivers; at <see cref="MaxDeliveryAttempts"/> failed attempts the message is dead-lettered. A
/// permanent failure dead-letters at once. A dependency-unavailable failure pauses the consuming host
/// for <see cref="DependencyUnavailableBackoff"/> and redelivers without counting. A message
/// interrupted by shutdown is redelivered with its count unchanged. Attempts and dead letters are
/// kept per endpoint: one message failing on one endpoint changes nothing for its copy on another.
/// </para>
/// <para>
/// Meant for tests, local development and single-process hosts. It is not durable, and it keeps
/// the failed-attempt count of every message that ever failed, for inspection, for its lifetime.
/// </para>
/// </remarks>
public sealed class InMemoryBroker
{
    private readonly ConcurrentDictionary<string, Endpoint> _endpoints = new(StringComparer.Ordinal);
    private readonly Lock _bindingsGate = new();
    private Dictionary<string, string[]> _bindings = new(StringComparer.Ordinal);

    private readonly int _maxDeliveryAttempts = 5;
    private readonly TimeSpan _dependencyUnavailableBackoff = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the number of failed attempts after which a message is dead-lettered on an endpoint.
    /// Defaults to 5; must be at least 1.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 1.</exception>
    public int MaxDeliveryAttempts
    {
        get => _maxDeliveryAttempts;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxDeliveryAttempts = value;
        }
    }

    /// <summary>
    /// Gets how long a consuming host pauses after a handler reports a dependency unavailable.
    /// Defaults to 5 seconds; must not be negative.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan DependencyUnavailableBackoff
    {
        get => _dependencyUnavailableBackoff;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _dependencyUnavailableBackoff = value;
        }
    }

    /// <summary>Gets a snapshot of an endpoint's dead-lettered messages, in dead-letter order.</summary>
    /// <param name="endpoint">The endpoint: a service name.</param>
    /// <returns>The dead letters; empty for an endpoint that has none or does not exist.</returns>
    public IReadOnlyList<MessageEnvelope> GetDeadLetters(string endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return _endpoints.TryGetValue(endpoint, out var e) ? [.. e.DeadLetters] : [];
    }

    /// <summary>
    /// Gets the number of failed attempts recorded for a message on an endpoint. Attempts
    /// interrupted by shutdown or by an unavailable dependency are not counted.
    /// </summary>
    /// <param name="endpoint">The endpoint: a service name.</param>
    /// <param name="messageId">The envelope's <see cref="MessageEnvelope.Id"/>.</param>
    /// <returns>The count, or 0 for a message that never failed there.</returns>
    public int GetFailedAttempts(string endpoint, string messageId)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(messageId);
        return _endpoints.TryGetValue(endpoint, out var e) ? e.FailedAttempts.GetValueOrDefault(messageId) : 0;
    }

    /// <summary>Binds an endpoint to wire types, creating it if needed. Bindings accumulate.</summary>
    internal void Bind(string endpoint, IEnumerable<string> wireTypes)
    {
        _ = EndpointOf(endpoint);

        lock (_bindingsGate)
        {
            // Copy-on-write: Publish reads the dictionary without the lock.
            var next = new Dictionary<string, string[]>(_bindings, StringComparer.Ordinal);
            foreach (var wireType in wireTypes)
            {
                var bound = next.GetValueOrDefault(wireType) ?? [];
                if (!bound.Contains(endpoint, StringComparer.Ordinal))
                {
                    next[wireType] = [.. bound, endpoint];
                }
            }

            _bindings = next;
        }
    }

    /// <summary>Gets the endpoints bound to a wire type, in binding order.</summary>
    internal IReadOnlyList<string> EndpointsBoundTo(string wireType)
        => Volatile.Read(ref _bindings).GetValueOrDefault(wireType) ?? [];

    /// <summary>Copies an envelope to every endpoint bound to its type.</summary>
    /// <returns>The number of endpoints it was copied to.</returns>
    internal int Publish(MessageEnvelope envelope)
    {
        var endpoints = EndpointsBoundTo(envelope.Type);
        foreach (var endpoint in endpoints)
        {
            EndpointOf(endpoint).Write(envelope);
        }

        return endpoints.Count;
    }

    /// <summary>
    /// Deposits an envelope on one endpoint, bypassing the routing. For tests that exercise the
    /// receive pipeline with an envelope no binding would route.
    /// </summary>
    internal void Deposit(string endpoint, MessageEnvelope envelope) => EndpointOf(endpoint).Write(envelope);

    internal ValueTask<MessageEnvelope> DequeueAsync(string endpoint, CancellationToken ct)
        => EndpointOf(endpoint).Queue.Reader.ReadAsync(ct);

    internal void Requeue(string endpoint, MessageEnvelope envelope) => EndpointOf(endpoint).Write(envelope);

    internal int RecordFailedAttempt(string endpoint, MessageEnvelope envelope)
        => EndpointOf(endpoint).FailedAttempts.AddOrUpdate(envelope.Id, 1, static (_, count) => count + 1);

    internal void DeadLetter(string endpoint, MessageEnvelope envelope)
        => EndpointOf(endpoint).DeadLetters.Enqueue(envelope);

    private Endpoint EndpointOf(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        return _endpoints.GetOrAdd(endpoint, static _ => new Endpoint());
    }

    /// <summary>One endpoint: its queue, its attempt counts and its dead letters.</summary>
    private sealed class Endpoint
    {
        public Channel<MessageEnvelope> Queue { get; } = Channel.CreateUnbounded<MessageEnvelope>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

        public ConcurrentDictionary<string, int> FailedAttempts { get; } = new(StringComparer.Ordinal);

        public ConcurrentQueue<MessageEnvelope> DeadLetters { get; } = new();

        // An unbounded channel that is never completed always accepts a write.
        public void Write(MessageEnvelope envelope) => _ = Queue.Writer.TryWrite(envelope);
    }
}
