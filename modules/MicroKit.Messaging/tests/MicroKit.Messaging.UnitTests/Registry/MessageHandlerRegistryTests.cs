namespace MicroKit.Messaging.UnitTests.Registry;

/// <summary>
/// Both lookup directions of <see cref="MessageHandlerRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this file exists now.</b> The by-event-type direction (<c>GetHandlers</c>) had exactly one
/// caller, the in-process fan-out, and was covered only through that caller's tests. ADR-MSG-019
/// deleted the fan-out, which would have left a public method with no caller <i>and</i> no coverage
/// — the state code rots in. It is kept rather than deleted because it is the seam the receiving
/// side needs (a wire name resolves to a local type, and that type resolves to its consumers here),
/// so it is tested directly instead.
/// </para>
/// <para>
/// The by-consumer-type direction is live: <c>InboxProcessor</c> uses it on every drained row.
/// </para>
/// </remarks>
public sealed class MessageHandlerRegistryTests
{
    private static readonly string ConsumerType =
        typeof(RecordingInboxHandler).AssemblyQualifiedName!;

    private static MessageHandlerRegistry WithOneHandler()
    {
        var registry = new MessageHandlerRegistry();
        registry.RegisterGeneric<InboxTestEvent>(ConsumerType, typeof(RecordingInboxHandler));
        return registry;
    }

    [Fact]
    public void GetHandlers_WhenRegistered_ReturnsTheEntryForTheEventType()
    {
        var entry = WithOneHandler().GetHandlers(typeof(InboxTestEvent)).ShouldHaveSingleItem();

        entry.ConsumerType.ShouldBe(ConsumerType);
        entry.HandlerType.ShouldBe(typeof(RecordingInboxHandler));
    }

    [Fact]
    public void GetHandlers_WhenNothingRegistered_ReturnsEmptyRatherThanThrowing()
    {
        // A missing subscriber is not an error — it is valid for an event to have no local
        // consumer in a multi-service deployment. The caller decides what to do about it.
        new MessageHandlerRegistry().GetHandlers(typeof(InboxTestEvent)).ShouldBeEmpty();
    }

    [Fact]
    public void GetHandlers_WhenTwoHandlersShareAnEventType_ReturnsBoth()
    {
        // One event, N consumers, one row each downstream. Collapsing them would silently drop a
        // consumer, which is the failure the compound dedup key exists to make impossible.
        var registry = WithOneHandler();
        registry.RegisterGeneric<InboxTestEvent>("second-consumer", typeof(SecondHandler));

        registry.GetHandlers(typeof(InboxTestEvent)).Count.ShouldBe(2);
    }

    /// <summary>
    /// Registering the same handler twice contributes one consumer, not two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An ordinary mistake in a composition root assembled from several module registrations, and
    /// silent before this: <c>EnvelopeReceiver</c> would fan one envelope out to the same consumer
    /// twice, the second write would hit the dedup index, and a <b>first</b> delivery would report
    /// <c>Duplicates = 1</c> while incrementing
    /// <c>microkit.inbox.messages.deduplicated</c> — the counter whose documented use is spotting a
    /// lease set too short or a broker replaying. A composition typo would read as a broker fault,
    /// at a steady rate, forever.
    /// </para>
    /// <para>
    /// The second assertion is the half that matters beyond the count: the surviving entry must be
    /// the <i>later</i> registration, so the two dictionaries cannot disagree about which invoker a
    /// consumer type resolves to.
    /// </para>
    /// </remarks>
    [Fact]
    public void RegisteringTheSameConsumerTwice_ContributesOneEntry()
    {
        var registry = WithOneHandler();
        registry.RegisterGeneric<InboxTestEvent>(ConsumerType, typeof(RecordingInboxHandler));

        registry.GetHandlers(typeof(InboxTestEvent)).Count.ShouldBe(
            1, "a doubled registration is one consumer, not a redelivery of every message to it");

        registry.RegisteredConsumerTypes.ShouldHaveSingleItem().ShouldBe(ConsumerType);
    }

    [Fact]
    public void ReRegisteringAConsumer_ReplacesTheEntryInBothLookups()
    {
        // Same consumer type name, different handler — a host rebinding a consumer. Replace rather
        // than ignore, so GetHandlers and TryGetInvoker cannot return different invokers for it.
        var registry = WithOneHandler();
        registry.RegisterGeneric<InboxTestEvent>(ConsumerType, typeof(SecondHandler));

        registry.GetHandlers(typeof(InboxTestEvent)).ShouldHaveSingleItem()
            .HandlerType.ShouldBe(typeof(SecondHandler));

        registry.TryGetInvoker(ConsumerType, out var entry).ShouldBeTrue();
        entry.HandlerType.ShouldBe(typeof(SecondHandler));
    }

    [Fact]
    public void TryGetInvoker_WhenRegistered_ReturnsTheEntry()
    {
        WithOneHandler().TryGetInvoker(ConsumerType, out var entry).ShouldBeTrue();

        entry.HandlerType.ShouldBe(typeof(RecordingInboxHandler));
    }

    [Fact]
    public void TryGetInvoker_WhenUnknownConsumerType_ReturnsFalse()
    {
        // InboxProcessor turns this into an InboxPayloadException and dead-letters on first sight:
        // a row naming a consumer this process does not have will never become processable by
        // waiting.
        new MessageHandlerRegistry().TryGetInvoker("nobody", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Invoker_InvokesTheHandlerWithTheDeserializedEvent()
    {
        // The invoker is a closure built at registration time and holds the only cast from object
        // to IMessageHandler<TEvent>. If it is wrong, nothing else in the registry reveals it.
        var registry = WithOneHandler();
        registry.TryGetInvoker(ConsumerType, out var entry).ShouldBeTrue();

        var handler = new RecordingInboxHandler();
        var evt = new InboxTestEvent(MessageId.New(), "tenant-1");

        await entry.Invoker(handler, evt, CancellationToken.None);

        handler.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public void Register_ReflectionOverload_ProducesAnEquivalentEntry()
    {
        // The overload that exists for callers with no compile-time type argument. It has no
        // production caller and never had one, so this is its only coverage.
        var registry = new MessageHandlerRegistry();
        registry.Register(typeof(InboxTestEvent), ConsumerType, typeof(RecordingInboxHandler));

        registry.GetHandlers(typeof(InboxTestEvent)).ShouldHaveSingleItem()
            .HandlerType.ShouldBe(typeof(RecordingInboxHandler));
        registry.TryGetInvoker(ConsumerType, out _).ShouldBeTrue();
    }

    [Fact]
    public void RegisteredConsumerTypes_IsEmptyBeforeAnyRegistration()
    {
        // Empty must mean empty. Nothing reads this in production since InboxIngestionValidator
        // was deleted with the receiving seam, so the direct coverage is what keeps it honest.
        new MessageHandlerRegistry().RegisteredConsumerTypes.ShouldBeEmpty();
    }

    [Fact]
    public void RegisteredConsumerTypes_NamesEveryDistinctConsumer()
    {
        var registry = WithOneHandler();
        registry.RegisterGeneric<InboxTestEvent>("second-consumer", typeof(SecondHandler));

        registry.RegisteredConsumerTypes.ShouldBe(
            [ConsumerType, "second-consumer"], ignoreOrder: true);
    }

    private sealed class SecondHandler : IMessageHandler<InboxTestEvent>
    {
        public ValueTask HandleAsync(InboxTestEvent evt, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
