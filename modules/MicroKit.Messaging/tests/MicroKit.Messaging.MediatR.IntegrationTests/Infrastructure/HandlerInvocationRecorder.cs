using System.Collections.Concurrent;

namespace MicroKit.Messaging.MediatR.IntegrationTests.Infrastructure;

/// <summary>
/// One recorded handler invocation.
/// </summary>
/// <param name="HandlerName">The invoked handler's type name.</param>
/// <param name="EventId">
/// The identity the invocation carried — the domain event's <c>EventId</c>, or the integration
/// event's <c>MessageId</c> — as a string, so outbox and inbox paths record comparably.
/// </param>
internal readonly record struct HandlerInvocation(string HandlerName, string EventId);

/// <summary>
/// Records handler invocations in call order. A real recorder rather than a mock: the assertions
/// are about observable behaviour, and NSubstitute's received-calls model answers "was it called"
/// far less directly than an ordered structured log answers "which handler ran, for which message,
/// how many times, in what order".
/// </summary>
/// <remarks>
/// <para>
/// Structured entries, not free-form strings: the question Phase 2 asks is "did handler 1 run twice
/// while handler 2 never ran at all", and that must be answerable by grouping on
/// <see cref="HandlerInvocation.HandlerName"/> and <see cref="HandlerInvocation.EventId"/> rather
/// than by matching formatted text.
/// </para>
/// <para>
/// Registered as a DI <strong>singleton</strong>, which is load-bearing. Notification and message
/// handlers run inside the per-message scope the outbox processor creates from the ROOT
/// <c>IServiceScopeFactory</c> — a different scope, and a different DbContext, from the one the
/// command used. A scoped recorder would be a fresh empty instance there and the test would observe
/// nothing.
/// </para>
/// </remarks>
internal sealed class HandlerInvocationRecorder
{
    private readonly ConcurrentQueue<HandlerInvocation> _invocations = new();

    /// <summary>Records one invocation. Call order is preserved.</summary>
    public void Record(string handlerName, string eventId)
        => _invocations.Enqueue(new HandlerInvocation(handlerName, eventId));

    /// <summary>The invocations recorded so far, in the order they happened.</summary>
    public IReadOnlyList<HandlerInvocation> Invocations => [.. _invocations];

    /// <summary>The invocations recorded by one handler type, in order.</summary>
    public IReadOnlyList<HandlerInvocation> For<THandler>()
        => [.. _invocations.Where(i => i.HandlerName == typeof(THandler).Name)];
}
