namespace MicroKit.Messaging.Spi;

/// <summary>
/// Delivers a <see cref="MessageEnvelope"/> arriving from a transport to every handler of its
/// contract, and says what the transport must do with it. The seam a broker provider's consume loop
/// calls.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <b>transport SPI</b> (<c>MicroKit.Messaging.Spi</c>): a broker provider implements
/// or drives this, and application code never touches it.
/// </para>
/// <para>
/// <b>A result whose <see cref="EnvelopeReceiveResult.Disposition"/> is
/// <see cref="ReceiveDisposition.Acknowledge"/> means every handler's outcome is durable and the
/// broker may be acknowledged</b> — not that the message was buffered for later. Every other
/// disposition means it must not be. This is the whole contract, and it is stated first because it
/// is the one thing a caller can get wrong invisibly.
/// </para>
/// <para>
/// It is the mirror of <see cref="IMessageTransport.SendAsync"/>'s rule. A provider that
/// acknowledges the broker before this method returns turns every handler not yet settled into a
/// message that no longer exists anywhere: the broker considers it delivered, this process never
/// handled it, and there is no log, no metric and no trace of the loss.
/// </para>
/// <para>
/// <b>The rule is a constraint on the client's CONFIGURATION, not merely on the order of your
/// calls.</b> "Acknowledge after the returned <see cref="ValueTask"/> completes, never alongside
/// it" is necessary but not sufficient, because the acknowledgement that loses the message is
/// usually one nobody wrote. Stated so it cannot be satisfied vacuously: <b>nothing may settle the
/// broker message except code that runs after the returned task has completed.</b> A consume loop
/// containing no acknowledgement call at all can be in full agreement with the ordering and in
/// full violation of the contract.
/// </para>
/// <para>
/// Concretely, for the brokers this module targets — the settling mechanism to disable is named,
/// because it is a setting rather than a statement:
/// <list type="bullet">
///   <item><b>Kafka</b> — <c>enable.auto.commit</c> is <b>on by default</b> and commits offsets on
///         a timer for messages <c>Consume</c> has already returned, so it can commit an offset
///         while <c>ReceiveAsync</c> is still in flight. Set it to <c>false</c> and commit after
///         the task completes. This is the one that needs no mistake to go wrong: leaving the
///         default in place is the mistake.</item>
///   <item><b>RabbitMQ</b> — <c>autoAck: true</c> on the consume call settles at dispatch. It is
///         set once at subscribe time, structurally far from anything that looks like an
///         acknowledgement, so the loop reads as correct. Subscribe with <c>autoAck: false</c> and
///         ack after the task completes.</item>
///   <item><b>Azure Service Bus</b> — <c>ReceiveMode.ReceiveAndDelete</c> settles at receive;
///         use <c>PeekLock</c>. <b><c>ServiceBusProcessor.AutoCompleteMessages</c>, which also
///         defaults to true, is safe</b> — it completes after the message handler returns, so
///         awaiting this method inside that handler satisfies the rule. The safe default and the
///         unsafe one both read as "auto", which is why both are named here.</item>
/// </list>
/// </para>
/// <para>
/// <b>Conformance obligation — owed by the first broker provider, and it is NOT literally the same
/// obligation <see cref="IMessageTransport"/> carries.</b> That one asserts a <i>callee's</i>
/// return: a test drives the provider's <c>SendAsync</c> and observes that it did not return early.
/// This one asserts a <i>caller's</i> ordering, which means instrumenting the acknowledgement
/// rather than the seam. Neither can be enforced from this package: no test here can observe
/// whether somebody else's consume loop settled the broker message before calling.
/// <list type="bullet">
///   <item>The first provider that drives this interface owes a conformance test that fails an
///         <see cref="IEnvelopeReceiver"/> — a stub whose returned task does not complete until
///         the test releases it — and asserts that <b>nothing has been settled at the broker</b>
///         while it is outstanding: no ack, no offset commit, no lock release, no delete.</item>
///   <item>It must run against the <i>real</i> client configuration the provider ships, not a hand
///         -rolled loop. The defects above live in client settings, so a test that mocks the client
///         away cannot see any of them.</item>
///   <item><b>A consume loop that fails that test is unusable</b> — regardless of whether it
///         compiles, and regardless of what its other tests show. It silently discards messages
///         this process never recorded.</item>
/// </list>
/// No conformance harness ships today because no provider exists to run one against; one written
/// with nothing to exercise it would only be designed twice. Meet this obligation while writing the
/// provider, not after an incident.
/// </para>
/// <para>
/// <b>The direction is the opposite of <see cref="IMessageTransport"/>, and that is the point.</b>
/// A provider implements <c>IMessageTransport</c> and <c>MicroKit.Messaging</c> calls it; here
/// <c>MicroKit.Messaging</c> implements the receiver and the provider's consume loop calls it. So
/// unlike the transport — of which the only implementation shipped is Core's in-memory one, and a
/// broker provider writes its own — an implementation of this interface does ship, and a provider
/// resolves it rather than writing one.
/// </para>
/// <para>
/// <b>Registered by <c>AddMicroKitMessaging(bus =&gt; ...)</c></b> (#187), next to the contract
/// registry this seam resolves a wire name through, and with no builder method of its own because
/// there is nothing to configure. The wire names it resolves are those of the types the service's
/// handlers handle and the types it publishes.
/// <para>
/// A provider's <c>Add{Provider}Transport()</c> may assume the receiver is present: it is an
/// extension on the builder <c>AddMicroKitMessaging</c> hands out, so the receiver is registered
/// before it runs. A host that registers no handler declares no subscription, so a wire name it
/// does not publish resolves to no local type and the receiver answers
/// <see cref="ReceiveDisposition.DeadLetter"/>.
/// </para>
/// </para>
/// <para>
/// <b>One pipeline (#188).</b> The receiver hands the envelope to the same receive pipeline as the
/// in-memory transport and a store's retry loop. <see cref="MessageEnvelope.Type"/> resolves to
/// <i>this</i> process's own CLR type through the contract registry — never to the producer's —
/// and that type to every handler registered for it, each run in its own scope.
/// </para>
/// <para>
/// <b>With a store, each handler is guarded by its inbox row</b>, keyed by
/// (<see cref="MessageEnvelope.Id"/>, handler): a row already there skips the handler; otherwise the
/// handler runs in a transaction that commits its work and its row
/// <see cref="InboxMessageStatus.Processed"/> together. A handler that fails has its row written
/// <see cref="InboxMessageStatus.Failed"/> in a transaction of its own, and the store retries it;
/// the message is acknowledged once that row is committed. With no store, a failure asks the
/// transport to redeliver.
/// </para>
/// <para>
/// <b>Do not call this inside a transaction</b> — one opened on the store's <c>DbContext</c> or an
/// ambient <c>System.Transactions.TransactionScope</c>. Each handler's transaction is the engine's
/// own; the consume loop is the outer boundary.
/// </para>
/// <para>
/// Registered as a <b>singleton</b>. It creates the scopes it needs internally, so a consume loop
/// may hold one instance for its lifetime and must not attempt to scope it.
/// </para>
/// </remarks>
/// <seealso cref="IMessageTransport"/>
public interface IEnvelopeReceiver
{
    /// <summary>
    /// Delivers one envelope to every handler of its contract and returns what the transport must do
    /// with it. See the interface remarks: acknowledge only on
    /// <see cref="ReceiveDisposition.Acknowledge"/>, and only once the returned task has completed.
    /// </summary>
    /// <param name="envelope">The message as it arrived from the transport.</param>
    /// <param name="ct">The consume loop's token; its cancellation means shutdown.</param>
    /// <returns>The disposition, with the reason and the error behind any other than
    /// <see cref="ReceiveDisposition.Acknowledge"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="envelope"/> is
    /// <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b><see cref="ReceiveDisposition.DeadLetter"/> on a wire type this process does not bind</b>
    /// is a property of <i>this</i> process acted on by a fleet: during a rolling deployment a
    /// message carrying a contract only the new version binds can land on an old instance. Deploy a
    /// consumer that binds a new contract before anything publishes it, and treat that dead-letter
    /// queue as requeueable once the rollout completes.
    /// </para>
    /// <para>
    /// Nothing about a message or a handler is thrown: every outcome is a disposition.
    /// </para>
    /// </remarks>
    ValueTask<EnvelopeReceiveResult> ReceiveAsync(
        MessageEnvelope envelope, CancellationToken ct = default);
}
