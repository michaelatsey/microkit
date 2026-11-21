namespace MicroKit.Messaging.UnitTests.Local;

/// <summary>
/// The verdicts of a local outbox row (#188 D7) that the pipeline cannot record on an inbox row: a row
/// no process can read is dead-lettered on first sight. They replace what
/// <c>MediatROutboxDispatcherTests</c> pinned for a notification row: an unresolvable type and an
/// unreadable payload are permanent, never retried.
/// </summary>
public sealed class LocalOutboxDispatcherTests
{
    private static async Task DispatchAsync(OutboxMessage row)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMicroKitMessaging(bus =>
        {
            bus.ServiceName = "tests.local";
            bus.AddMessageStore(new MessageStoreKey("tests:local"), _ => { });
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredKeyedService<IOutboxDispatcher>(OutboxDispatchers.Local).DispatchAsync(row);
    }

    private static OutboxMessage LocalRow(string eventType, string payload)
    {
        var row = OutboxFixtures.Message();
        row.Endpoint = OutboxMessage.LocalEndpoint;
        row.EventType = eventType;
        row.Payload = payload;
        return row;
    }

    [Fact]
    public async Task ARowWhoseTypeResolvesNowhere_IsPermanent()
    {
        var fault = await Should.ThrowAsync<OutboxPayloadException>(
            () => DispatchAsync(LocalRow("Nowhere.Type, Nowhere", "{}")));

        fault.Message.ShouldContain("Nowhere.Type");
    }

    [Fact]
    public async Task ARowWhosePayloadCannotBeRead_IsPermanent()
    {
        await Should.ThrowAsync<OutboxPayloadException>(
            () => DispatchAsync(LocalRow(typeof(LocalParcel).AssemblyQualifiedName!, "{ not json")));
    }

    [Fact]
    public async Task ARowNoConsumerHandles_IsDispatched_AndNothingRuns()
    {
        // The pipeline's verdict for a message no handler consumes: acknowledged — the row is
        // Published — and reported (event 3001), never retried.
        await DispatchAsync(LocalRow(typeof(LocalParcel).AssemblyQualifiedName!, """{"name":"p"}"""));
    }

    public sealed record LocalParcel(string Name);
}
