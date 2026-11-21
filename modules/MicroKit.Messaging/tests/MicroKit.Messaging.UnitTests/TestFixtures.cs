using MicroKit.Messaging.Execution;

namespace MicroKit.Messaging.UnitTests;

/// <summary>
/// In-process <see cref="IExecutionScopeFactory"/> for unit tests. Wraps the test
/// <see cref="IServiceScopeFactory"/> with no tenant hydration.
/// </summary>
internal sealed class TestExecutionScopeFactory(IServiceScopeFactory scopeFactory) : IExecutionScopeFactory
{
    /// <summary>Every context handed to <see cref="CreateScopeAsync"/>, in order.</summary>
    /// <remarks>
    /// A recording fake, not a substitute. The assertions this exists for are about what a processor
    /// PUTS into the context — <c>TenantId</c>, <c>CorrelationId</c>, and now a derived
    /// <c>CausationId</c>. A mock's argument matcher stays green whenever it names a property the
    /// subject does not set, which is exactly the defect that let the causation chain be null on
    /// every row without a single red test.
    /// </remarks>
    public List<IExecutionContext> Contexts { get; } = [];

    public ValueTask<IExecutionScope> CreateScopeAsync(
        IExecutionContext context, CancellationToken ct = default)
    {
        Contexts.Add(context);

        return ValueTask.FromResult<IExecutionScope>(
            new TestExecutionScope(scopeFactory.CreateAsyncScope()));
    }
}

internal sealed class TestExecutionScope(AsyncServiceScope scope) : IExecutionScope
{
    public IServiceProvider ServiceProvider => scope.ServiceProvider;
    public ValueTask DisposeAsync() => scope.DisposeAsync();
}

/// <summary>
/// A <see cref="Random"/> whose draw is fixed, so the outbox back-off can be asserted against
/// exact values instead of a range.
/// </summary>
/// <remarks>
/// <para>
/// The processor applies full jitter as <c>ceiling.Ticks * NextDouble()</c>. A draw of
/// <c>1.0</c> therefore neutralises the jitter exactly — the delay IS the ceiling — which is
/// what lets the exponential curve and the <c>MaxRetryBackoff</c> cap be pinned. A draw of
/// <c>0.0</c> collapses the delay to zero.
/// </para>
/// <para>
/// <see cref="Random"/> is used directly rather than a bespoke interface: it is already an
/// ordinary class with a <see langword="virtual"/> <see cref="Random.NextDouble"/>, so it
/// substitutes exactly the way <see cref="TimeProvider"/> does, and nothing new has to be
/// published or maintained. Production registers <see cref="Random.Shared"/>.
/// </para>
/// <para>
/// Note that <c>1.0</c> is outside the <c>[0,1)</c> range the real
/// <see cref="Random.NextDouble"/> contract promises. That is deliberate and safe here: it is the
/// only draw that makes the jitter a no-op, and this type exists solely to be substituted in.
/// </para>
/// </remarks>
internal sealed class FixedRandom(double next) : Random
{
    /// <summary>A draw that neutralises jitter: the delay equals the full back-off ceiling.</summary>
    public static FixedRandom NoJitter => new(1.0);

    /// <summary>A draw that collapses jitter to zero delay.</summary>
    public static FixedRandom ZeroDelay => new(0.0);

    public override double NextDouble() => next;
}

/// <summary>
/// An <see cref="IOutboxDispatcher"/> that records what it saw and throws whatever a script
/// tells it to for a given message.
/// </summary>
/// <remarks>
/// Hand-written rather than an NSubstitute mock because the tests care about the ORDER of
/// dispatches and about throwing a different exception per message — both awkward to express
/// as call-sequence assertions.
/// </remarks>
internal sealed class ScriptedDispatcher : IOutboxDispatcher
{
    private readonly Func<OutboxMessage, Exception?>? _script;

    public ScriptedDispatcher(Func<OutboxMessage, Exception?>? script = null) => _script = script;

    /// <summary>Every message this dispatcher was actually handed, in order.</summary>
    public List<OutboxMessage> Dispatched { get; } = [];

    public ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct = default)
    {
        Dispatched.Add(message);

        var fault = _script?.Invoke(message);
        return fault is null ? ValueTask.CompletedTask : ValueTask.FromException(fault);
    }
}

/// <summary>Builders for outbox rows and claims, so tests state only what they care about.</summary>
internal static class OutboxFixtures
{
    internal static OutboxMessage Message(
        int retryCount = 0,
        string eventType = "MicroKit.Test.TestEvent, MicroKit.Test",
        string? tenantId = "tenant-a")
        => new()
        {
            Id = MessageId.New(),
            TenantId = tenantId,
            EventType = eventType,
            Payload = "{}",
            Status = OutboxMessageStatus.Processing,
            RetryCount = retryCount,
            OccurredOnUtc = DateTimeOffset.UnixEpoch,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            CorrelationId = CorrelationId.New(),
        };

    /// <summary>
    /// A transport row — one the transport dispatcher can actually send.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Message"/> rather than a parameter on it, because a transport row
    /// must carry <c>ContractName</c> and <c>Source</c>: a single builder with nullable defaults
    /// would let a test silently construct a row the bus never writes.
    /// </remarks>
    internal static OutboxMessage ContractMessage(
        string? contractName = "shop.orders.order-placed.v1",
        string? source = "/shop/orders",
        string payload = """{"orderId":"e0b3a1f2-0000-4000-8000-000000000001"}""",
        string? tenantId = "tenant-a",
        int retryCount = 0)
    {
        var message = Message(retryCount: retryCount, tenantId: tenantId);
        message.ContractName = contractName;
        message.Source = source;
        message.Payload = payload;
        message.CausationId = CausationId.New();
        return message;
    }

    internal static OutboxClaim Claim(params OutboxMessage[] messages)
        => new(Guid.NewGuid(), messages);
}

/// <summary>
/// An <see cref="IMessageTransport"/> that records every envelope it was handed and throws
/// whatever a script tells it to.
/// </summary>
/// <remarks>
/// <b>A recording fake rather than an NSubstitute mock, deliberately.</b> Several of these tests
/// assert that nothing was sent, and a negative assertion against a mock only holds if it names a
/// method the subject actually calls — <c>DidNotReceive().SendAsync(...)</c> against code that
/// calls something else is green whatever happens, and no mutation exposes it. Asserting that
/// <see cref="Sent"/> is empty cannot be satisfied that way.
/// </remarks>
internal sealed class RecordingMessageTransport(Func<MessageEnvelope, Exception?>? script = null)
    : IMessageTransport
{
    /// <summary>Every envelope this transport was handed, in order.</summary>
    public List<MessageEnvelope> Sent { get; } = [];

    public ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        var fault = script?.Invoke(envelope);
        if (fault is not null)
        {
            return ValueTask.FromException(fault);
        }

        Sent.Add(envelope);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Builders for inbox rows and claims, so tests state only what they care about.</summary>
internal static class InboxFixtures
{
    internal const string DefaultEventType = "MicroKit.Test.TestEvent, MicroKit.Test";

    internal static InboxMessage Message(
        MessageId? messageId = null,
        string consumerType = "MicroKit.Test.TestHandler, MicroKit.Test",
        int retryCount = 0,
        string eventType = DefaultEventType,
        string payload = "{}",
        string? tenantId = "tenant-a",
        CorrelationId? correlationId = null)
        => new()
        {
            RowId = Guid.NewGuid(),
            MessageId = messageId ?? MessageId.New(),
            ConsumerType = consumerType,
            TenantId = tenantId,
            EventType = eventType,
            Payload = payload,
            Status = InboxMessageStatus.Processing,
            RetryCount = retryCount,
            ReceivedAtUtc = DateTimeOffset.UnixEpoch,

            // Null by default, which is legitimate: an inbound message from an external system may
            // carry no correlation context (ADR-MSG-017, and the deliberate nullability asymmetry
            // with OutboxMessage.CorrelationId). A test that asserts the copy must pass one.
            CorrelationId = correlationId,
        };

    internal static InboxClaim Claim(params InboxMessage[] messages)
        => new(Guid.NewGuid(), messages);

    internal static InboxMessageKey KeyOf(InboxMessage message)
        => new(message.MessageId, message.ConsumerType);
}

/// <summary>
/// The "database" behind <see cref="InMemoryInboxGuard"/>: the committed inbox rows, shared by every
/// scope, with scripts for the faults a real store raises.
/// </summary>
internal sealed class InMemoryInboxDatabase
{
    private readonly object _gate = new();

    /// <summary>Gets the committed rows.</summary>
    public Dictionary<InboxMessageKey, InboxMessage> Rows { get; } = [];

    /// <summary>Gets or sets a fault raised by the commit of an attempt, once, before anything is written.</summary>
    public Exception? FailNextCommit { get; set; }

    /// <summary>Gets or sets a fault raised by the commit of an attempt, once, after everything is written.</summary>
    public Exception? FailAfterNextCommit { get; set; }

    /// <summary>Gets or sets a fault raised by every Failed-row insert.</summary>
    public Exception? FailInsertFailed { get; set; }

    /// <summary>Gets or sets the faults the guard's strategy retries; none by default.</summary>
    public Func<Exception, bool> RetryOn { get; set; } = _ => false;

    /// <summary>Gets the number of attempts begun.</summary>
    public int AttemptsBegun { get; private set; }

    /// <summary>Gets or sets whether every read fails: the database is down.</summary>
    public bool FailReads { get; set; }

    public InboxMessage? Find(InboxMessageKey key)
    {
        lock (_gate)
        {
            if (FailReads)
            {
                throw new TimeoutException("the database is unreachable");
            }

            return Rows.GetValueOrDefault(key);
        }
    }

    public void Add(InboxMessage row)
    {
        lock (_gate)
        {
            Rows[new InboxMessageKey(row.MessageId, row.ConsumerType)] = row;
        }
    }

    internal void Begun()
    {
        lock (_gate)
        {
            AttemptsBegun++;
        }
    }

    internal void Commit(InboxMessage? enlisted, (InboxMessageKey Key, Guid Token)? claimed, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (FailNextCommit is { } fault)
            {
                FailNextCommit = null;
                throw fault;
            }

            if (enlisted is not null)
            {
                var key = new InboxMessageKey(enlisted.MessageId, enlisted.ConsumerType);
                if (Rows.ContainsKey(key))
                {
                    throw new DuplicateKeyTestException();
                }

                Rows[key] = enlisted;
            }

            if (claimed is { } mark)
            {
                if (!Rows.TryGetValue(mark.Key, out var row) || row.ClaimToken != mark.Token)
                {
                    throw new LeaseLostTestException();
                }

                row.Status = InboxMessageStatus.Processed;
                row.ProcessedAtUtc = now;
                row.ClaimToken = null;
                row.LockedUntilUtc = null;
            }

            if (FailAfterNextCommit is { } after)
            {
                FailAfterNextCommit = null;
                throw after;
            }
        }
    }

    internal bool TryInsert(InboxMessage row)
    {
        lock (_gate)
        {
            if (FailInsertFailed is { } fault)
            {
                throw fault;
            }

            var key = new InboxMessageKey(row.MessageId, row.ConsumerType);
            if (Rows.ContainsKey(key))
            {
                return false;
            }

            Rows[key] = row;
            return true;
        }
    }
}

/// <summary>The unique-index violation of <see cref="InMemoryInboxDatabase"/>.</summary>
internal sealed class DuplicateKeyTestException() : Exception("duplicate (MessageId, ConsumerType)");

/// <summary>The lost claim of <see cref="InMemoryInboxDatabase"/>: the conditioned write matched no row.</summary>
internal sealed class LeaseLostTestException() : Exception("the claim token no longer matches");

/// <summary>
/// An <see cref="IInboxGuardStore"/> over <see cref="InMemoryInboxDatabase"/>, scoped like the real
/// one: what an attempt enlists is written only by <see cref="CommitAsync"/>, so an attempt that does
/// not commit leaves nothing.
/// </summary>
/// <remarks>
/// Its execution strategy runs the operation once — no retrying strategy — unless
/// <see cref="InMemoryInboxDatabase.RetryOn"/> names the faults to retry, which then go through the verification first, as
/// a retrying EF strategy does.
/// </remarks>
internal sealed class InMemoryInboxGuard(InMemoryInboxDatabase database, TimeProvider clock) : IInboxGuardStore
{
    private InboxMessage? _enlisted;
    private (InboxMessageKey Key, Guid Token)? _claimed;
    private bool _begun;

    public ValueTask<InboxRowState?> FindAsync(InboxMessageKey key, CancellationToken ct = default)
        => ValueTask.FromResult(database.Find(key) is { } row
            ? new InboxRowState(row.Status, row.DeadLettered, row.RowId)
            : (InboxRowState?)null);

    public async ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        Func<CancellationToken, ValueTask<bool>> verifySucceeded,
        TResult verifiedResult,
        CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(ct);
            }
            catch (Exception ex) when (attempt < 3 && database.RetryOn(ex))
            {
                if (await verifySucceeded(ct))
                {
                    return verifiedResult;
                }
            }
        }
    }

    public ValueTask BeginAsync(CancellationToken ct = default)
    {
        _begun = true;
        database.Begun();
        return ValueTask.CompletedTask;
    }

    public ValueTask EnlistProcessedAsync(InboxMessage row, CancellationToken ct = default)
    {
        _enlisted = row;
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> MarkClaimedProcessedAsync(InboxMessageKey key, Guid claimToken, CancellationToken ct = default)
    {
        if (database.Find(key)?.ClaimToken != claimToken)
        {
            return ValueTask.FromResult(false);
        }

        _claimed = (key, claimToken);
        return ValueTask.FromResult(true);
    }

    public ValueTask CommitAsync(CancellationToken ct = default)
    {
        if (!_begun)
        {
            throw new InvalidOperationException("Committed before it was begun.");
        }

        database.Commit(_enlisted, _claimed, clock.GetUtcNow());
        return ValueTask.CompletedTask;
    }

    public bool IsLeaseLost(Exception exception) => exception is LeaseLostTestException;

    public ValueTask<bool> TryInsertFailedAsync(InboxMessage row, CancellationToken ct = default)
        => ValueTask.FromResult(database.TryInsert(row));
}

/// <summary>A handler that records invocations and can be scripted to throw.</summary>
internal sealed class RecordingInboxHandler : IMessageHandler<InboxTestEvent>
{
    private int _invocationCount;

    public int InvocationCount => _invocationCount;

    /// <summary>Gets or sets the exception thrown on every invocation, if any.</summary>
    public Exception? ThrowOnHandle { get; set; }

    /// <summary>Gets the context of the most recent invocation, if any.</summary>
    public MessageContext? LastContext { get; private set; }

    public ValueTask HandleAsync(InboxTestEvent message, MessageContext context, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _invocationCount);
        LastContext = context;

        if (ThrowOnHandle is not null)
        {
            throw ThrowOnHandle;
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>The integration event the inbox unit tests round-trip.</summary>
internal sealed record InboxTestEvent(MessageId MessageId, string TenantId) : IIntegrationEvent
{
    public DateTimeOffset OccurredOnUtc { get; } = DateTimeOffset.UnixEpoch;
}

/// <summary>One log record, flattened to what an assertion needs.</summary>
internal readonly record struct CapturedLog(LogLevel Level, int EventId, string Message, Exception? Exception = null);

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps every record, so "silent success is forbidden" can be
/// asserted rather than assumed.
/// </summary>
/// <remarks>
/// <see cref="IsEnabled"/> must return <see langword="true"/> unconditionally: the
/// <c>[LoggerMessage]</c> generator inserts its own guard, so a logger that reports a level as
/// disabled makes the call vanish and the assertion vacuous.
/// </remarks>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>Every record written, in order.</summary>
    public List<CapturedLog> Records { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Records.Add(new CapturedLog(logLevel, eventId.Id, formatter(state, exception), exception));
}

/// <summary>Reads the #186 envelope the way a test asserts on it.</summary>
internal static class EnvelopeReading
{
    /// <summary>The body as UTF-8 text.</summary>
    public static string BodyText(this MessageEnvelope envelope)
        => System.Text.Encoding.UTF8.GetString(envelope.Body.Span);

    /// <summary>A header's value, or null when absent.</summary>
    public static string? Header(this MessageEnvelope envelope, string name)
        => envelope.Headers.TryGetValue(name, out var value) ? value : null;
}
