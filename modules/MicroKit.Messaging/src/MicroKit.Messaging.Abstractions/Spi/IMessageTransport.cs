namespace MicroKit.Messaging.Spi;

/// <summary>
/// Moves a <see cref="MessageEnvelope"/> out of this process to its destination. The seam a broker
/// provider implements.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <b>transport SPI</b> (<c>MicroKit.Messaging.Spi</c>): a broker provider implements
/// or drives this, and application code never touches it.
/// </para>
/// <para>
/// <b>Returning from <see cref="SendAsync"/> without throwing means the destination has
/// acknowledged the message.</b> Not that it was enqueued for background delivery, not that it was
/// written to a local buffer, not that a fire-and-forget call was made. This is the whole contract,
/// and it is the first thing stated here because it is the one thing an implementation can get
/// wrong invisibly.
/// </para>
/// <para>
/// It is not a stylistic preference. <c>OutboxProcessor</c> records the message as
/// <see cref="OutboxMessageStatus.Published"/> the moment this method returns, and
/// <c>Published</c> is <b>terminal</b> — the row is never claimed again. A transport that hands off
/// asynchronously turns that mark into a lie in exactly the way a transactional outbox exists to
/// prevent: a publish confirmation that never arrives becomes indistinguishable from one that did,
/// and the message is gone with no log, no metric and no trace of it.
/// </para>
/// <para>
/// Concretely, for the brokers this module targets: RabbitMQ — return after the <i>publisher
/// confirm</i>, not after <c>BasicPublishAsync</c> returns; Azure Service Bus — after
/// <c>SendMessagesAsync</c> completes; Kafka — after the delivery report, with the acknowledgement
/// level the deployment requires.
/// </para>
/// <para>
/// <b>Conformance obligation — owed by the first broker provider.</b> The rule above cannot be
/// enforced from this package: no test here can observe whether an implementation returned before
/// its broker confirmed. It is therefore stated as a requirement rather than left to be
/// rediscovered.
/// <list type="bullet">
///   <item>The first provider that implements this interface owes a conformance test asserting
///         that <see cref="SendAsync"/> does not return before the broker has acknowledged the
///         message.</item>
///   <item><b>A transport that fails that test is unusable</b> — regardless of whether it compiles,
///         and regardless of what its other tests show. It silently converts every
///         <c>Published</c> mark in the producing system into a lie.</item>
/// </list>
/// No conformance harness ships today because no provider exists to run one against; one written
/// with nothing to exercise it would only be designed twice. Meet this obligation while writing the
/// provider, not after an incident.
/// </para>
/// <para>
/// <b>One implementation ships: Core's in-memory transport</b> (#186), registered by
/// <c>MessagingBuilder.UseInMemoryTransport()</c> together with the consume loop that runs every
/// envelope through the receive pipeline. A send that returns is a message the transport holds, and
/// the guarantee goes no further than the transport's own durability: while the process runs, the
/// in-memory broker hands every message it holds to the pipeline, which handles or dead-letters it,
/// but it is not durable and loses the messages still queued when the process exits. A broker
/// provider supplies its own implementation from
/// its <c>Add{Provider}Transport()</c> extension. With no transport registered at all, a
/// <see cref="MessageKind.Contract"/> row fails loudly and reversibly — see
/// <c>MessagingBuilder.AddTransportDispatcher</c> — and a bus publication throws.
/// </para>
/// <para>
/// <b>Lifetime.</b> <c>UseInMemoryTransport()</c> registers the in-memory transport as a
/// <b>singleton</b>: the bus resolves it from any publishing scope, and <c>OutboxProcessor</c>
/// resolves it through the dispatcher in each per-message execution scope. A provider may register
/// its transport scoped or singleton, but it is resolved from both kinds of scope, so it must not
/// depend on state of the scope it was first resolved in.
/// </para>
/// </remarks>
public interface IMessageTransport
{
    /// <summary>
    /// Sends one envelope and returns only once the destination has acknowledged it. See the
    /// interface remarks — that guarantee is the contract, not a quality of service.
    /// </summary>
    /// <param name="envelope">The message to send, in its wire form.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// A <see cref="ValueTask"/> that completes when the destination has acknowledged the message.
    /// </returns>
    /// <exception cref="OutboxTransportUnavailableException">
    /// The transport itself is unreachable — connection refused, broker shut down, authentication
    /// rejected, channel closed — so sending the <i>next</i> message is certain to fail too.
    /// <para>
    /// This says something about the transport, not about this message, which is why it must be
    /// raised only when that is genuinely true. The processor abandons the whole batch on it and
    /// releases every unattempted message without consuming a retry. Without that distinction one
    /// outage fails every claimed message, writes a failure row for each, burns each retry budget,
    /// and repeats on the next tick until the queue has dead-lettered itself.
    /// </para>
    /// </exception>
    /// <exception cref="OutboxPayloadException">
    /// The destination rejects this message permanently for its content — larger than the broker's
    /// limit, or a contract name a schema registry refuses. The processor dead-letters on first
    /// sight, because no retry can change a row already persisted.
    /// <para>
    /// <b>Never raise this for</b> a nack, a timeout, a refused connection, an HTTP 503 or any
    /// other infrastructure fault. Only proven permanence dead-letters; a transport that guesses
    /// permanence wrongly discards messages that would have succeeded a second later.
    /// </para>
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct"/> was signalled. The processor releases the remainder of the batch
    /// without consuming retries.
    /// </exception>
    /// <remarks>
    /// A single message rejected while the transport is otherwise healthy is an ordinary transient
    /// failure: let it propagate untyped, and the processor retries it with jittered back-off until
    /// <c>MaxRetries</c>. That is the default and it is deliberately conservative — everything
    /// unrecognised stays transient.
    /// <para>
    /// Never raise <see cref="OutboxConfigurationException"/> <b>from a transport</b>. That
    /// classification means the composition is wrong, and it stops the worker; raising it from a
    /// send would stop the worker over a runtime fault, which is the one failure a transport is
    /// most likely to meet and least able to distinguish.
    /// <para>
    /// The ban is on transports specifically, not on the exception having a single origin — two
    /// things inside the engine do raise it, and both establish the fault before any send is
    /// attempted. The processor raises it when a service it resolves from the scope is not
    /// registered at all — <c>GetService</c> returns <see langword="null"/> — which the container
    /// decides rather than a parsed message. A missing <see cref="IMessageTransport"/> behind a
    /// <i>registered</i> dispatcher is not that case: the container throws
    /// <see cref="InvalidOperationException"/> while activating the dispatcher, and the processor
    /// releases the batch without rethrowing
    /// (<c>OutboxBatchAbortReason.DispatcherActivationFailed</c>). An
    /// <c>IOutboxDispatcher</c> may also raise it for
    /// a row it <i>structurally cannot serve in this composition</i>: <c>TransportOutboxDispatcher</c>
    /// does so for a <see cref="MessageKind.Notification"/> row, which is a perfectly good row that
    /// only <c>MicroKit.Messaging.MediatR</c> can fan out, so its arrival proves that package is
    /// not registered. Both are properties of the composition, decided before the message is sent.
    /// A transport's failures never are: by the time it runs, the composition has already been
    /// proven adequate by the fact that the transport was resolved and called at all.
    /// </para>
    /// </para>
    /// <para>
    /// <b>What the constructor throws is classified the same way, with one exception.</b> The
    /// transport is resolved from the per-message scope, so a constructor that fails does so while the
    /// dispatcher is being activated. A typed exception from there reaches the same verdict it would
    /// from this method, and an untyped one is retried per message — except
    /// <see cref="InvalidOperationException"/>, the type the container uses for a dependency it cannot
    /// supply, which is reported as <c>OutboxBatchAbortReason.DispatcherActivationFailed</c> and
    /// abandons the batch. A client that knows its broker is unreachable should say so with
    /// <see cref="OutboxTransportUnavailableException"/> rather than let that type escape.
    /// </para>
    /// </remarks>
    ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default);
}
