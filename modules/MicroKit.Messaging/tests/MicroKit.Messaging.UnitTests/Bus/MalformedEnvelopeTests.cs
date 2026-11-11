namespace MicroKit.Messaging.UnitTests.Bus;

using MicroKit.Messaging.Serialization;
using MicroKit.Messaging.Transport;

/// <summary>
/// A failure before the per-handler stage never escapes the receive pipeline (#186, PR #202
/// review): shutdown leaves the message unsettled, anything else dead-letters it, and the consume
/// loop keeps running. The sender refuses a malformed envelope outright.
/// </summary>
public sealed class MalformedEnvelopeTests
{
    [Fact]
    public async Task AnEnvelopeWithNullHeaders_IsDeadLettered_AndTheNextMessageIsStillConsumed()
    {
        await using var host = await BusTestHost.StartAsync(
            bus => bus.AddHandler<FirstOrderPlacedHandler>());

        // Hand-built and enqueued past the sender's validation, as an envelope mapped by a
        // provider could arrive: `required` is a compile-time promise only.
        var valid = Envelope();
        var malformed = new MessageEnvelope
        {
            Id = valid.Id,
            Source = valid.Source,
            Type = valid.Type,
            Time = valid.Time,
            DataContentType = valid.DataContentType,
            Headers = null!,
            Body = valid.Body,
        };
        host.Broker.Deposit(host.ServiceName, malformed);

        await Eventually.HoldsAsync(() => host.DeadLetters.Count == 1, "dead-lettered");
        host.DeadLetters.Single().Id.ShouldBe(malformed.Id);

        await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        await Eventually.HoldsAsync(
            () => host.Recorder.Deliveries.Count == 1, "the consume loop survived and consumed the next");
    }

    [Fact]
    public async Task ASerializerThrowingOperationCanceledOutsideShutdown_DeadLettersTheMessage()
    {
        var serializer = new ScriptedDeserializer { CancelOnce = true };
        await using var host = await BusTestHost.StartAsync(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(),
            services: services => services.AddSingleton<IMessageSerializer>(serializer));

        var poisoned = await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));

        await Eventually.HoldsAsync(() => host.DeadLetters.Count == 1, "dead-lettered");
        host.DeadLetters.Single().Id.ShouldBe(poisoned.ToString());
        host.FailedAttempts(poisoned.ToString()).ShouldBe(0, "permanent, not retried");

        await host.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        await Eventually.HoldsAsync(
            () => host.Recorder.Deliveries.Count == 1, "the consume loop survived and consumed the next");
    }

    [Fact]
    public async Task ASerializerThrowingOperationCanceledDuringShutdown_RequeuesWithTheAttemptCountUnchanged()
    {
        var broker = new InMemoryBroker();
        var serializer = new ScriptedDeserializer { BlockOnce = true };

        MessageId id;
        await using (var first = await BusTestHost.StartAsync(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(),
            broker,
            services => services.AddSingleton<IMessageSerializer>(serializer)))
        {
            id = await first.PublishAsync(new OrderPlaced(Guid.NewGuid()));
            await serializer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // StopAsync cancels the host token synchronously, before its first await; only then is
            // the blocked Deserialize released, and it throws OperationCanceledException.
            var stopping = first.StopAsync();
            serializer.Release.Set();
            await stopping;

            first.Recorder.Deliveries.ShouldBeEmpty();
        }

        broker.GetDeadLetters(BusTestHost.Source).ShouldBeEmpty("shutdown is never classified");
        broker.GetFailedAttempts(BusTestHost.Source, id.ToString()).ShouldBe(0, "shutdown is not a failed attempt");

        await using var second = await BusTestHost.StartAsync(
            bus => bus.AddHandler<FirstOrderPlacedHandler>(), broker);

        await Eventually.HoldsAsync(
            () => second.Recorder.Deliveries.Count == 1, "the requeued message reaches the next host");
        second.Recorder.Deliveries.Single().Context.MessageId.ShouldBe(id);
        broker.GetFailedAttempts(BusTestHost.Source, id.ToString()).ShouldBe(0);
    }

    public static TheoryData<string> RequiredMembers =>
        [nameof(MessageEnvelope.Id), nameof(MessageEnvelope.Source), nameof(MessageEnvelope.Type),
         nameof(MessageEnvelope.DataContentType), nameof(MessageEnvelope.Headers)];

    [Theory]
    [MemberData(nameof(RequiredMembers))]
    public async Task SendAsync_RejectsAnEnvelopeMissingARequiredMember(string member)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = BusTestHost.Source;
            bus.UseInMemoryTransport();
        });
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IMessageTransport>();
        var valid = Envelope();

        var malformed = new MessageEnvelope
        {
            Id = member == nameof(MessageEnvelope.Id) ? null! : valid.Id,
            Source = member == nameof(MessageEnvelope.Source) ? null! : valid.Source,
            Type = member == nameof(MessageEnvelope.Type) ? " " : valid.Type,
            Time = valid.Time,
            DataContentType = member == nameof(MessageEnvelope.DataContentType) ? "" : valid.DataContentType,
            Headers = member == nameof(MessageEnvelope.Headers) ? null! : valid.Headers,
            Body = valid.Body,
        };

        var ex = await Should.ThrowAsync<ArgumentException>(async () => await transport.SendAsync(malformed));

        // The LEADING words, case-sensitive: every message ends with "an id, a source, a type and
        // a content type", so a contains-check would pass for a check that names the wrong member.
        ex.Message.ShouldStartWith($"The envelope's {member} ", Case.Sensitive);
        await transport.SendAsync(valid);   // CONTROL: a valid envelope is accepted.
    }

    private static MessageEnvelope Envelope() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Source = BusTestHost.Source,
        Type = "tests.bus.order-placed.v1",
        Time = DateTimeOffset.UnixEpoch,
        DataContentType = "application/json",
        Body = """{"orderId":"cccccccc-cccc-4ccc-8ccc-cccccccccccc"}"""u8.ToArray(),
    };
}

/// <summary>
/// The default serializer, except that its first deserialization throws
/// <see cref="OperationCanceledException"/> — at once, or after blocking until released.
/// </summary>
internal sealed class ScriptedDeserializer : IMessageSerializer
{
    private readonly SystemTextJsonMessageSerializer _inner = new();
    private int _calls;

    public bool CancelOnce { get; init; }

    public bool BlockOnce { get; init; }

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ManualResetEventSlim Release { get; } = new();

    public string ContentType => _inner.ContentType;

    public ReadOnlyMemory<byte> Serialize(object message, Type type) => _inner.Serialize(message, type);

    public object? Deserialize(ReadOnlyMemory<byte> body, Type type)
    {
        if (Interlocked.Increment(ref _calls) == 1 && (CancelOnce || BlockOnce))
        {
            if (BlockOnce)
            {
                Entered.TrySetResult();
                Release.Wait(TimeSpan.FromSeconds(10));
            }

            throw new OperationCanceledException("cancelled inside the serializer");
        }

        return _inner.Deserialize(body, type);
    }
}
