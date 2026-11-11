using System.Text;
using System.Text.Json;

namespace MicroKit.Messaging.MediatR.IntegrationTests;

/// <summary>
/// One handler implementing <see cref="IMessageHandler{T}"/> for two message types, delivered
/// through the inbox (#187, decision 2). The registry is keyed on (consumer type, message type):
/// keyed on the consumer alone, the second registration replaced the first, and the inbox handed a
/// row of one type to the invoker of the other — a cast failure, dead-lettered as permanent. The
/// bus half of the guarantee is in <c>SingleEntryPointTests</c>.
/// </summary>
public sealed class MultiTypeHandlerInboxTests
{
    [Fact]
    public async Task AHandlerOfTwoTypes_ThroughTheInbox_ReachesEachOfItsMethods_AndNothingIsDeadLettered()
    {
        await using var connection = await E2EHarness.OpenConnectionAsync();
        await E2EHarness.CreateSchemaAsync(connection);
        var calls = new TwoTypeCalls();

        await using var provider = E2EHarness.BuildProvider(
            connection, new HandlerInvocationRecorder(), new CapturingLoggerProvider(), messaging =>
            {
                messaging.Services.AddSingleton(calls);
                messaging.AddHandler<TwoTypeInboxHandler>();
            });

        var receiver = provider.GetRequiredService<IEnvelopeReceiver>();
        var widget = new WidgetShipped(Guid.NewGuid());
        var gadget = new GadgetShipped(Guid.NewGuid());

        (await receiver.ReceiveAsync(Envelope(WidgetShipped.ContractName, widget))).RowsAdded.ShouldBe(1);
        (await receiver.ReceiveAsync(Envelope(GadgetShipped.ContractName, gadget))).RowsAdded.ShouldBe(1);

        await E2EHarness.DrainInboxOnceAsync(provider, CancellationToken.None);

        calls.Received.ShouldBe(
            [$"widget:{widget.WidgetId}", $"gadget:{gadget.GadgetId}"],
            ignoreOrder: true);

        var inbox = await E2EHarness.ReadInboxAsync(connection);
        inbox.Count.ShouldBe(2);
        inbox.Select(r => r.ConsumerType).Distinct().ShouldHaveSingleItem(
            "one consumer, one ConsumerType: the message type is the other half of the key");
        inbox.ShouldAllBe(r => r.Status == InboxMessageStatus.Processed && !r.DeadLettered);
    }

    private static MessageEnvelope Envelope(string type, object message) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Source = "/partner/shipping",
        Type = type,
        Time = DateTimeOffset.UtcNow,
        DataContentType = "application/json",
        Headers = new Dictionary<string, string>(StringComparer.Ordinal),
        Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, message.GetType(), JsonSerializerOptions.Web)),
    };
}

[MessageContract(Name)]
internal sealed record GadgetShipped(Guid GadgetId) : IIntegrationEvent
{
    internal const string Name = "microkit.e2e.gadget-shipped";

    internal const string ContractName = Name + ".v1";
}

internal sealed class TwoTypeCalls
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _received = new();

    public IReadOnlyList<string> Received => [.. _received];

    public void Record(string call) => _received.Enqueue(call);
}

internal sealed class TwoTypeInboxHandler(TwoTypeCalls calls)
    : IMessageHandler<WidgetShipped>, IMessageHandler<GadgetShipped>
{
    public ValueTask HandleAsync(WidgetShipped message, MessageContext context, CancellationToken ct = default)
    {
        calls.Record($"widget:{message.WidgetId}");
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(GadgetShipped message, MessageContext context, CancellationToken ct = default)
    {
        calls.Record($"gadget:{message.GadgetId}");
        return ValueTask.CompletedTask;
    }
}
