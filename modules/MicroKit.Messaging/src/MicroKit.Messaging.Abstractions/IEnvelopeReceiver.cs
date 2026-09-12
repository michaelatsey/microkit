namespace MicroKit.Messaging;

/// <summary>
/// Records a <see cref="MessageEnvelope"/> arriving from a transport into the inbox, one row per
/// registered consumer. The seam a broker provider's consume loop calls.
/// </summary>
/// <remarks>
/// <para>
/// <b>Returning from <see cref="ReceiveAsync"/> without throwing means the message is durably
/// recorded and the broker may be acknowledged.</b> Not that it was accepted for background
/// recording, not that it was buffered in memory. This is the whole contract, and it is stated
/// first because it is the one thing a caller can get wrong invisibly.
/// </para>
/// <para>
/// It is the mirror of <see cref="IMessageTransport.SendAsync"/>'s rule. A provider that
/// acknowledges the broker before this method returns turns every row it did not yet write into a
/// message that no longer exists anywhere: the broker considers it delivered, this process never
/// recorded it, and there is no log, no metric and no trace of the loss.
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
/// unlike the transport — of which <b>no</b> implementation ships in any MicroKit package — an
/// implementation of this interface does ship, and a provider resolves it rather than writing one.
/// </para>
/// <para>
/// <b>Registered by <c>AddIntegrationEventConsumption()</c> or
/// <c>AddIntegrationEventPublishing()</c></b> — either is enough, in any order, and there is no
/// builder method of its own because there is nothing to configure. Both of those are where the
/// contract registry this seam resolves a wire name through is composed, which is why the receiver
/// belongs with them and not in <c>AddMicroKitMessaging()</c>.
/// <para>
/// A provider's <c>Add{Provider}Transport()</c> may assume the receiver is present, but that is a
/// convention rather than a guarantee the container enforces: a host that called
/// <c>AddMicroKitMessaging()</c> and <c>AddTransportDispatcher()</c> and neither
/// integration-event entry point declares no contracts, so a wire name could not resolve to a
/// local type even if the seam were registered. A provider resolving it then gets the container's
/// own "no service for type" message, which names the interface but not the call that supplies it
/// — so a provider is better off resolving it eagerly at startup and saying which method is
/// missing than discovering it on the first delivery.
/// </para>
/// </para>
/// <para>
/// <b>The fan-out is: contract name → local type → consumers.</b>
/// <see cref="MessageEnvelope.ContractName"/> resolves to <i>this</i> process's own CLR type
/// through the contract registry — never to the producer's, which a consumer does not hold — and
/// that type resolves to every <c>IMessageHandler&lt;T&gt;</c> registered for it. Each consumer
/// gets its own <see cref="InboxMessage"/>, keyed by
/// (<see cref="MessageEnvelope.MessageId"/>, <c>ConsumerType</c>), so the rows advance
/// independently: one handler may succeed while another retries.
/// </para>
/// <para>
/// <b>A redelivery is nominal, not a failure.</b> Under at-least-once delivery one expired lease
/// after a crash is enough to produce one. The unique index absorbs it, the affected consumer is
/// counted in <see cref="EnvelopeReceiveResult.Duplicates"/>, and <b>the remaining consumers still
/// get their rows</b> — a duplicate for one must never cost the consumers after it theirs. Nothing
/// is thrown for it.
/// </para>
/// <para>
/// <b>On the transaction boundary — and why it is the opposite of
/// <see cref="IIntegrationEventPublisher"/>.</b> That interface requires a transaction the caller
/// already owns and never commits; this one requires none and commits before returning. The
/// asymmetry is not an inconsistency. On the publishing side the caller has a transaction carrying
/// business writes, and a contract row must join it or announce a fact that may still roll back. On
/// the receiving side the provider's consume loop <i>is</i> the outer boundary: there are no
/// business writes in flight and nothing to join, and the durability the acknowledgement rule
/// demands can only come from a write that has already committed.
/// </para>
/// <para>
/// Consequently there is no atomicity <i>across</i> the fan-out, and none is wanted. A failure
/// after the second consumer's row means the first is recorded and the message is redelivered; the
/// first is then absorbed as a duplicate and the rest are written. That is the same at-least-once
/// property the inbox exists to make safe, applied to its own ingestion.
/// </para>
/// <para>
/// <b>Do not call this inside an ambient <c>System.Transactions.TransactionScope</c> either</b> —
/// a stronger statement than the paragraph above, which rules out a transaction the caller opened
/// on the <c>DbContext</c>. An enlisted scope is invisible to the store: it inspects
/// <c>Database.CurrentTransaction</c>, which stays null under one. Three things then fail at once
/// and none of them reports it — the rows are not durable when this method returns, so the
/// acknowledgement rule above is broken; no savepoint is taken, so an absorbed duplicate aborts
/// the ambient transaction on PostgreSQL; and the guard that would otherwise refuse a provider
/// without savepoint support is bypassed. Let the consume loop be the outer boundary, which is
/// what it already is.
/// </para>
/// <para>
/// Registered as a <b>singleton</b>. It creates one execution scope per envelope internally, so a
/// consume loop may hold one instance for its lifetime and must not attempt to scope it: sharing a
/// scope across messages would share a <c>DbContext</c>, and a fault on one message would corrupt
/// the next.
/// </para>
/// </remarks>
/// <seealso cref="IMessageTransport"/>
/// <seealso cref="IIntegrationEventPublisher"/>
public interface IEnvelopeReceiver
{
    /// <summary>
    /// Records one envelope as an inbox row per registered consumer, returning only once every row
    /// is durably committed. See the interface remarks — that guarantee is the contract, not a
    /// quality of service.
    /// </summary>
    /// <param name="envelope">The message as it arrived from the transport.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// The per-consumer tally: rows written, redeliveries absorbed, and how many consumers the
    /// contract matched at all.
    /// </returns>
    /// <exception cref="InboxPayloadException">
    /// <see cref="MessageEnvelope.ContractName"/> is bound to no local type in this process.
    /// <para>
    /// <b>A provider must dead-letter this message, never nack it for retry.</b> The binding is
    /// established at composition, so it cannot appear without a redeployment and no number of
    /// redeliveries will change the verdict reached on the first. And unlike every other permanent
    /// inbox fault there is <i>no row to dead-letter</i> — nothing was written, so the verdict has
    /// nowhere to live but the broker. A message nacked for retry here loops until the broker's own
    /// limit and is then discarded with no record that this process ever refused it.
    /// </para>
    /// <para>
    /// ⚠ <b>Read <see cref="InboxPayloadException"/>'s own remarks before writing that
    /// dead-letter branch.</b> The permanence is a property of <i>a process</i> and is acted on by
    /// <i>a fleet</i>: during a rolling deployment both versions consume the same queue, so a
    /// message carrying a contract only the new version binds can land on an old instance and be
    /// dead-lettered when the instance beside it would have accepted it. The classification stays,
    /// and the consequences are operational — deploy a consumer that binds a new contract before
    /// anything publishes it, and treat this dead-letter queue as requeueable once the rollout
    /// completes. A provider inherits that rule verbatim and should restate it in its own docs.
    /// </para>
    /// </exception>
    /// <exception cref="InboxConfigurationException">
    /// <see cref="IInboxWriter"/> is not registered, so nothing can be recorded at all. A
    /// deployment defect, never a runtime fault: a provider should let it stop the consume loop
    /// rather than nack a message forever against a composition no redelivery can fix.
    /// <para>
    /// <b>Not registered, specifically — not "the writer could not be produced".</b> A writer that
    /// is registered and fails to activate raises whatever its activation raised, and that
    /// propagates unchanged into the "any other exception" rule below. The distinction is
    /// load-bearing under a tenant-aware <c>IExecutionScopeFactory</c>, where an envelope naming
    /// an unknown tenant fails the connection resolution: that is one
    /// message's data fault, to be nacked, and reporting it here would stop ingestion for every
    /// tenant.
    /// </para>
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="envelope"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct"/> was signalled. Rows already written stay written; the message must not
    /// be acknowledged, and its redelivery is absorbed by the dedup gate.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="IInboxWriter"/> reported an <see cref="InboxWriteResult"/> this implementation
    /// does not classify — a defect in this module rather than anything about the message or the
    /// composition.
    /// <para>
    /// Called out separately because the catch-all rule below would otherwise route it to
    /// redelivery, and a module defect does not resolve on a retry: the message would loop until
    /// the broker's own limit. It is unreachable while <see cref="InboxWriteResult"/> has its two
    /// documented values, and it is raised rather than absorbed precisely so a third value cannot
    /// be counted as a row written. Treat it as an unrecoverable fault: stop the consume loop and
    /// report it, rather than nacking.
    /// </para>
    /// </exception>
    /// <remarks>
    /// Any other exception is a real failure of the recording itself — a lost connection, a
    /// timeout, a constraint violation that is not the dedup gate, a registered
    /// <see cref="IInboxWriter"/> that could not be activated. The message is <b>not</b> recorded
    /// and must not be acknowledged; nack it for redelivery. Only
    /// <see cref="InboxPayloadException"/> is proven permanent, and a provider that guesses
    /// permanence more broadly discards messages that would have succeeded a second later.
    /// </remarks>
    ValueTask<EnvelopeReceiveResult> ReceiveAsync(
        MessageEnvelope envelope, CancellationToken ct = default);
}
