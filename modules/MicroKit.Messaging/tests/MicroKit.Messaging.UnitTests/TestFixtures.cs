using MicroKit.Messaging.Execution;

namespace MicroKit.Messaging.UnitTests;

/// <summary>
/// In-process <see cref="IExecutionScopeFactory"/> for unit tests. Wraps the test
/// <see cref="IServiceScopeFactory"/> with no tenant hydration.
/// </summary>
internal sealed class TestExecutionScopeFactory(IServiceScopeFactory scopeFactory) : IExecutionScopeFactory
{
    public ValueTask<IExecutionScope> CreateScopeAsync(
        IExecutionContext context, CancellationToken ct = default)
        => ValueTask.FromResult<IExecutionScope>(
            new TestExecutionScope(scopeFactory.CreateAsyncScope()));
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

    internal static OutboxClaim Claim(params OutboxMessage[] messages)
        => new(Guid.NewGuid(), messages);
}
