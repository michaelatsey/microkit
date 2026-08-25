namespace MicroKit.Messaging;

/// <summary>
/// The wire form of one integration message: what a transport hands to a broker, and what a
/// receiving process is given back.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is a compatibility commitment, and the commitment is at the MEMBER level.</b>
/// Once a message has travelled, every member below is something a consumer written today must
/// still be able to read a year from now — including a consumer built by someone who does not
/// share this assembly, this repository or this release cadence. Removing or renaming one is a
/// breaking change to every deployed consumer at once. Treat a change here the way you would treat
/// a change to a published HTTP contract.
/// </para>
/// <para>
/// <b>What this package does not own is the byte encoding.</b> Nothing here serializes an
/// envelope: <c>TransportOutboxDispatcher</c> hands this object to
/// <see cref="IMessageTransport.SendAsync"/>, and the provider behind that seam decides how it
/// reaches the broker. Property casing, whether a null member is written or omitted, and the
/// <see cref="DateTimeOffset"/> format are therefore a provider's decisions rather than this
/// type's — so the member set is what this type guarantees, and the bytes are what a provider
/// guarantees.
/// <list type="bullet">
///   <item><b>The first broker provider owes that decision explicitly</b> — casing, null
///         omission, timestamp format — rather than inheriting whatever its serializer happens to
///         default to.</item>
///   <item><b>Once one ships, its encoding is the de-facto standard</b>, and every later provider
///         matches it. A second provider that chooses differently splits the wire format in two
///         while every member name still agrees, which is the harder version of this problem:
///         nothing about the C# type looks wrong.</item>
/// </list>
/// No canonical encoder ships today, deliberately — there is no provider to use one, and one
/// written with nothing to exercise it would only be designed twice.
/// </para>
/// <para>
/// <b>How a member is added, decided in advance rather than by whoever needs one first.</b> The
/// primary constructor signature below is <b>frozen</b>. A new member is declared as an
/// <c>init</c> property in the record body, with a default:
/// <code>
/// public sealed record MessageEnvelope(/* … unchanged … */)
/// {
///     public string? TraceParent { get; init; }
/// }
/// </code>
/// Adding a parameter to the primary constructor instead would be source- <i>and</i>
/// binary-breaking for every <c>new MessageEnvelope(…)</c> call site — which is exactly where an
/// inbound receiver builds one from a broker message — and would change the deconstructor's arity
/// with it. "Additive" holds on the wire, where a consumer that does not know a member ignores it;
/// it does not hold for a public positional record, and conflating the two is how the first
/// addition becomes a break. <c>with</c> expressions, value equality and
/// <c>System.Text.Json</c> all treat a body-declared <c>init</c> property exactly as they treat a
/// positional one.
/// </para>
/// <para>
/// <b>The payload is opaque.</b> <see cref="Payload"/> is the JSON exactly as it was serialized at
/// staging, carried through byte for byte. No stage of the send path deserializes it: the row was
/// written by <c>IMessageSerializer.Serialize</c> in the producing process, so materializing it
/// again would resolve a type, build an object and re-serialize it to the same bytes — while making
/// the send path depend on the producer's type graph, which is the one thing that does not cross a
/// process boundary. Deserialization belongs to the receiver, which resolves
/// <see cref="ContractName"/> to its <i>own</i> local type through
/// <c>IntegrationEventRegistry</c>.
/// </para>
/// <para>
/// <b><see cref="Payload"/> is a <c>string</c>, which commits the wire to a text payload.</b> An
/// accepted constraint rather than an oversight: <c>IMessageSerializer.Serialize</c> returns a
/// string, so the row already holds text, and a JSON body is what every broker this module targets
/// carries without ceremony. A binary format later means base64 into this member or a second member
/// beside it — recorded here so that cost is met as a known trade rather than as a surprise.
/// </para>
/// <para>
/// <b>Built only from a <see cref="MessageKind.Contract"/> row.</b> Several members are
/// non-nullable here although the corresponding <see cref="OutboxMessage"/> columns are nullable,
/// and the difference is the point rather than an inconsistency: the row permits null because a
/// <see cref="MessageKind.Notification"/> row genuinely has no wire identity, while an envelope
/// without one is unaddressable and must not be constructible. The dispatcher enforces the
/// transition and raises <see cref="OutboxPayloadException"/> on a row that cannot make it.
/// </para>
/// <para>
/// <b>The identifiers are <see cref="Guid"/>s here, not the strongly-typed
/// <see cref="MicroKit.Messaging.MessageId"/> / <see cref="MicroKit.Messaging.CorrelationId"/> /
/// <see cref="MicroKit.Messaging.CausationId"/> records used everywhere else in this module.</b>
/// That is deliberate, and it is a decision about the wire rather than a lapse in typing. Those
/// records are positional wrappers, so a reflection-based serializer emits them as
/// <c>{"value":"…"}</c> — a C# implementation detail that a Python, Node or Java consumer would
/// then have to read as <c>envelope.messageId.value</c>, forever, because nothing can remove it
/// once a consumer depends on it.
/// <para>
/// The obvious alternative — a <c>JsonConverter</c> flattening them — was rejected because it
/// would push this decision down into serializer <i>configuration</i>, where a provider
/// registering its own serializer, or a swap to the planned source-generated one, silently drops
/// it. The declared member type is the one part of the encoding this package can fix from here,
/// which is precisely why it is the part worth spending: casing and null handling belong to a
/// provider (see above), the shape of an identifier does not. (Such a converter could not live
/// here in any case: <c>IMessageSerializer</c>'s own docs record that <c>System.Text.Json</c> is
/// not permitted in this package.)
/// </para>
/// <para>
/// The strong types stay where they earn their keep — on <see cref="OutboxMessage"/>,
/// <see cref="InboxMessage"/> and every producer-side API. They are converted at this boundary and
/// nowhere else. This type is a wire DTO whose entire purpose is to be serialized; it is the one
/// place where the primitive is the more honest declaration.
/// </para>
/// </para>
/// <para>
/// <b>What deliberately does not travel</b>, recorded so the omissions are not re-derived as
/// oversights:
/// <list type="bullet">
///   <item><see cref="OutboxMessage.EventType"/> — an assembly-qualified CLR name. Shipping it
///         would invite a receiver to call <c>Type.GetType</c> on it, which succeeds inside a
///         monolith and fails the day the producing module is extracted into its own service. That
///         accident is precisely what <see cref="ContractName"/> exists to remove, so excluding it
///         is load-bearing.</item>
///   <item><see cref="OutboxMessage.SourceMessageId"/> — the producer's own replay key, internal to
///         its outbox. A consumer deduplicates on <see cref="MessageId"/>; exposing the producer's
///         reentrancy model on the wire would freeze it there.</item>
///   <item>A trace parent — the right thing to propagate, and
///         <c>IntegrationEventMessage.TraceParent</c> already captures one, but
///         <see cref="OutboxMessage"/> carries no such column yet. A member that could only ever be
///         null is worse than an absent one, because a consumer builds on it. Additive later.</item>
///   <item>Delivery bookkeeping — retry count, status, claim token, lease expiry. The producer's
///         business, never the consumer's.</item>
///   <item>An envelope version. A version member is only useful behind a versioning policy, and
///         there is none; a receiver would treat "absent" as v1 regardless, which is what it does
///         without the member. Version lives in the contract name's <c>.v1</c> suffix.</item>
/// </list>
/// </para>
/// </remarks>
/// <param name="MessageId">
/// The delivery identity, and the consumer's idempotency key — the inbox deduplicates on it
/// together with the consumer type. Carries the value of the row's
/// <see cref="MicroKit.Messaging.MessageId"/>; see the type remarks for why it is a bare
/// <see cref="Guid"/> here.
/// <para>
/// This is the <b>producing outbox row's</b> <see cref="OutboxMessage.Id"/>, never a value
/// generated per send. It has to be: a redelivered dispatch must produce the same key, or the
/// receiver's unique index has nothing to recognise the duplicate by. Deriving it from the
/// deserialized payload instead only ever survived a retry by accident, and closing that was the
/// point of ADR-MSG-018.
/// </para>
/// </param>
/// <param name="ContractName">
/// The stable wire identity, e.g. <c>saasbtp.safety.constat-recorded.v1</c> — the only name a
/// receiving process can act on, and the one thing about this message that must survive a CLR
/// rename, an assembly split or a module extraction.
/// </param>
/// <param name="Source">
/// The emitting module, e.g. <c>/saasbtp/safety</c>.
/// <para>
/// Identifies the module and not the deployment, so it stays constant when that module is extracted
/// into its own service — which is what makes the extraction a non-event for consumers. It also
/// gives a receiver something to route and filter on without parsing a naming convention out of
/// <paramref name="ContractName"/>, and pairs with <paramref name="MessageId"/> as a uniqueness key
/// spanning several producers, in the manner of the CloudEvents <c>(source, id)</c> rule.
/// </para>
/// </param>
/// <param name="Payload">The serialized business payload. Opaque — see the type remarks.</param>
/// <param name="TenantId">
/// The tenant this message belongs to, or <see langword="null"/> in a single-tenant deployment.
/// <para>
/// Nullable, matching <see cref="OutboxMessage.TenantId"/> and ADR-MSG-008 §5: messaging must run
/// without Tenancy, and null is the correct value there rather than a missing one. It travels
/// because a consumer in a shared-database multi-tenant deployment cannot process a message without
/// knowing whose it is, and there is no ambient context on the far side of a boundary to recover it
/// from.
/// </para>
/// </param>
/// <param name="CorrelationId">
/// The request chain this message belongs to, or <see langword="null"/> when the producer had none.
/// Crossing an asynchronous boundary is where a trace link is most valuable and least
/// reconstructible.
/// </param>
/// <param name="CausationId">
/// The message that caused this one, or <see langword="null"/> for a root event.
/// </param>
/// <param name="OccurredOnUtc">
/// When the underlying business fact occurred — not when the row was staged, and not when it was
/// sent. A consumer ordering or windowing on this reads the business timeline rather than the
/// relay's clock.
/// </param>
public sealed record MessageEnvelope(
    Guid MessageId,
    string ContractName,
    string Source,
    string Payload,
    string? TenantId,
    Guid? CorrelationId,
    Guid? CausationId,
    DateTimeOffset OccurredOnUtc);
