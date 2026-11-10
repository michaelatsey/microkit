namespace MicroKit.Messaging;

using System.Collections.Concurrent;
using System.Threading.Channels;

/// <summary>
/// The queue behind the in-memory transport: one endpoint, held in process memory.
/// </summary>
/// <remarks>
/// <para>
/// <b>A host may supply its own</b> through <see cref="MessagingBuilder.UseInMemoryTransport"/>,
/// so that two successive hosts share one queue: a message the first host left unsettled at
/// shutdown is delivered by the second. Nothing survives the process.
/// </para>
/// <para>
/// <b>Redelivery is bounded.</b> A transient failure counts an attempt and redelivers; at
/// <see cref="MaxDeliveryAttempts"/> failed attempts the message is dead-lettered. A permanent
/// failure dead-letters at once. A dependency-unavailable failure pauses the endpoint for
/// <see cref="DependencyUnavailableBackoff"/> and redelivers without counting. A message
/// interrupted by shutdown is redelivered with its count unchanged.
/// </para>
/// <para>
/// <b>One competing-consumer queue.</b> Two hosts running on one broker <i>at the same time</i>
/// take messages from each other, and a host that does not know a message's contract dead-letters
/// it as permanent. Share a broker between hosts that run one after the other, or that compose the
/// same contracts.
/// </para>
/// <para>
/// Meant for tests, local development and single-process hosts. It is not durable, and it keeps
/// the failed-attempt count of every message that ever failed, for inspection, for its lifetime.
/// </para>
/// </remarks>
public sealed class InMemoryBroker
{
    private readonly Channel<MessageEnvelope> _queue = Channel.CreateUnbounded<MessageEnvelope>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    private readonly ConcurrentDictionary<string, int> _failedAttempts =
        new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<MessageEnvelope> _deadLetters = new();

    private readonly int _maxDeliveryAttempts = 5;
    private readonly TimeSpan _dependencyUnavailableBackoff = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the number of failed attempts after which a message is dead-lettered. Defaults to 5;
    /// must be at least 1.
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
    /// Gets how long the endpoint pauses after a handler reports a dependency unavailable.
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

    /// <summary>Gets a snapshot of the dead-lettered messages, in dead-letter order.</summary>
    public IReadOnlyList<MessageEnvelope> DeadLetters => [.. _deadLetters];

    /// <summary>
    /// Gets the number of failed attempts recorded for a message. Attempts interrupted by shutdown
    /// or by an unavailable dependency are not counted.
    /// </summary>
    /// <param name="messageId">The envelope's <see cref="MessageEnvelope.Id"/>.</param>
    /// <returns>The count, or 0 for a message that never failed.</returns>
    public int GetFailedAttempts(string messageId)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        return _failedAttempts.GetValueOrDefault(messageId);
    }

    internal ValueTask EnqueueAsync(MessageEnvelope envelope, CancellationToken ct)
        => _queue.Writer.WriteAsync(envelope, ct);

    internal ValueTask<MessageEnvelope> DequeueAsync(CancellationToken ct)
        => _queue.Reader.ReadAsync(ct);

    internal void Requeue(MessageEnvelope envelope)
    {
        // An unbounded channel that is never completed always accepts a write.
        _ = _queue.Writer.TryWrite(envelope);
    }

    internal int RecordFailedAttempt(MessageEnvelope envelope)
        => _failedAttempts.AddOrUpdate(envelope.Id, 1, static (_, count) => count + 1);

    internal void DeadLetter(MessageEnvelope envelope) => _deadLetters.Enqueue(envelope);
}
