namespace MicroKit.Messaging.Local;

using System.Collections.Concurrent;
using System.Threading.Channels;

/// <summary>
/// The in-memory queue of the local endpoint for a host with no store (#188 D7): one entry per
/// (message, consumer), what <see cref="LocalQueueWorker"/> delivers, with each entry's attempt count
/// and the dead-letter count.
/// </summary>
/// <remarks>
/// <para>
/// <b>One entry per consumer</b>, as a store delivers once per consumer under its own inbox row (D8):
/// a consumer that fails is redelivered alone, and one that succeeded never runs again. The entries
/// of one message share its row by reference — its payload is serialized once, by the sender.
/// </para>
/// <para>
/// Per entry, the #186 in-process semantics: a transient failure redelivers it, up to
/// <see cref="MaxDeliveryAttempts"/> failed attempts, then dead-letters it; a permanent one
/// dead-letters it at once; a dependency outage pauses the worker for
/// <see cref="DependencyUnavailableBackoff"/> and redelivers it without counting.
/// </para>
/// <para>
/// <b>Not durable.</b> A message is queued when it is sent, before the sender's work commits, and is
/// lost if the process stops — the price of a host with no store.
/// </para>
/// </remarks>
internal sealed class LocalQueue
{
    private readonly Channel<LocalEntry> _queue = Channel.CreateUnbounded<LocalEntry>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<(Guid MessageId, string ConsumerType), int> _failedAttempts = new();
    private int _deadLettered;

    /// <summary>Gets the failed attempts after which an entry is dead-lettered. Five by default.</summary>
    public int MaxDeliveryAttempts
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 5;

    /// <summary>Gets the pause after a dependency outage. Five seconds by default.</summary>
    public TimeSpan DependencyUnavailableBackoff
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(5);

    /// <summary>Gets how many entries were dead-lettered.</summary>
    /// <remarks>
    /// A count, not the entries: each dead letter is logged with its id, type, consumer and reason
    /// (<c>LocalLogs.DeadLettered</c>, event 2302), and a host with no store keeps nothing it could requeue, so
    /// holding the payloads would only grow memory for as long as the process runs.
    /// </remarks>
    public int DeadLettered => Volatile.Read(ref _deadLettered);

    /// <summary>Queues a message for one of its consumers.</summary>
    /// <param name="message">The local message, as an unsaved outbox row, shared by its entries.</param>
    /// <param name="consumerType">The consumer this entry delivers to.</param>
    public void Enqueue(OutboxMessage message, string consumerType)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrEmpty(consumerType);
        _queue.Writer.TryWrite(new LocalEntry(message, consumerType));
    }

    /// <summary>Waits for the next entry.</summary>
    /// <param name="ct">The worker's stopping token.</param>
    /// <returns>The entry.</returns>
    public ValueTask<LocalEntry> DequeueAsync(CancellationToken ct) => _queue.Reader.ReadAsync(ct);

    /// <summary>Puts an entry back at the end of the queue, its count unchanged.</summary>
    /// <param name="entry">The entry.</param>
    public void Requeue(LocalEntry entry) => _queue.Writer.TryWrite(entry);

    /// <summary>Counts one failed attempt of an entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>Its failed attempts so far.</returns>
    public int RecordFailedAttempt(LocalEntry entry)
        => _failedAttempts.AddOrUpdate(entry.Key, 1, static (_, n) => n + 1);

    /// <summary>Gets an entry's failed attempts.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The count.</returns>
    public int FailedAttempts(LocalEntry entry) => _failedAttempts.GetValueOrDefault(entry.Key);

    /// <summary>Dead-letters an entry: it is never delivered again.</summary>
    /// <param name="entry">The entry.</param>
    public void DeadLetter(LocalEntry entry)
    {
        _failedAttempts.TryRemove(entry.Key, out _);
        Interlocked.Increment(ref _deadLettered);
    }

    /// <summary>Forgets a delivered entry's count.</summary>
    /// <param name="entry">The entry.</param>
    public void Delivered(LocalEntry entry) => _failedAttempts.TryRemove(entry.Key, out _);
}

/// <summary>One (message, consumer) delivery of the in-memory local queue.</summary>
/// <param name="Message">The message's row, shared by reference by every entry of the message.</param>
/// <param name="ConsumerType">The consumer it is delivered to.</param>
internal readonly record struct LocalEntry(OutboxMessage Message, string ConsumerType)
{
    /// <summary>Gets the entry's identity: the message and the consumer, as an inbox row's key.</summary>
    public (Guid MessageId, string ConsumerType) Key => (Message.Id.Value, ConsumerType);
}
