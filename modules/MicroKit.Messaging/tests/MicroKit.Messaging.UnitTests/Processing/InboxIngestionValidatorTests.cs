namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// <c>InboxIngestionValidator</c> turns an undetectable gap into a startup failure: handlers
/// registered while nothing in this release produces the inbox rows that would reach them
/// (ADR-MSG-019).
/// </summary>
/// <remarks>
/// <para>
/// Driven directly rather than by standing up a host, for the reason
/// <c>IntegrationEventRegistryValidator</c> gives: starting one would start four messaging workers
/// needing stores these tests have no reason to wire.
/// </para>
/// <para>
/// That is also why every other test in the suite composes with <c>AddMessageHandler</c> freely —
/// <c>BuildServiceProvider()</c> starts no hosted service, so this check only reaches a real host.
/// </para>
/// </remarks>
public sealed class InboxIngestionValidatorTests
{
    private static InboxIngestionValidator Build(MessageHandlerRegistry registry)
    {
        var services = new ServiceCollection();
        services.AddSingleton(registry);
        return new InboxIngestionValidator(services.BuildServiceProvider());
    }

    [Fact]
    public async Task StartAsync_WhenNoHandlersRegistered_DoesNotThrow()
    {
        // The composition this release supports: outbox and transport, no in-process consumers.
        // It must start cleanly, or the validator would break every host rather than the one it
        // is aimed at.
        await Should.NotThrowAsync(
            async () => await Build(new MessageHandlerRegistry()).StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_WhenAHandlerIsRegistered_ThrowsInboxConfigurationException()
    {
        var registry = new MessageHandlerRegistry();
        registry.RegisterGeneric<InboxTestEvent>("some-consumer", typeof(RecordingInboxHandler));

        await Should.ThrowAsync<InboxConfigurationException>(
            async () => await Build(registry).StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_NamesTheRegisteredConsumersAndTheReason()
    {
        // A count alone tells an operator the host is misconfigured without telling them which
        // registration to remove. The message has to carry both the offending consumer and why it
        // is unreachable, because nothing else in the system will say so.
        var registry = new MessageHandlerRegistry();
        registry.RegisterGeneric<InboxTestEvent>("some-consumer", typeof(RecordingInboxHandler));

        var ex = await Should.ThrowAsync<InboxConfigurationException>(
            async () => await Build(registry).StartAsync(CancellationToken.None));

        ex.Message.ShouldContain("some-consumer");
        ex.Message.ShouldContain("AddMessageHandler");
    }

    [Fact]
    public async Task StopAsync_IsANoOp()
    {
        await Should.NotThrowAsync(
            async () => await Build(new MessageHandlerRegistry()).StopAsync(CancellationToken.None));
    }
}
