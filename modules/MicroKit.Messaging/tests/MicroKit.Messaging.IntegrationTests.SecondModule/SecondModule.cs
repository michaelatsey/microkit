using System.Collections.Concurrent;

namespace MicroKit.Messaging.IntegrationTests.SecondModule;

/// <summary>The marker a store binds this assembly by.</summary>
public sealed class SecondModuleMarker;

/// <summary>A message type declared in this assembly.</summary>
[MessageContract("tests.second-module.event")]
public sealed record SecondModuleEvent(Guid Id) : IIntegrationEvent;

/// <summary>Every <see cref="SecondModuleEvent"/> handled, with when it was handled.</summary>
public sealed class SecondModuleRecorder
{
    public ConcurrentQueue<(Guid Id, DateTimeOffset HandledAt)> Handled { get; } = new();
}

/// <summary>A handler declared in this assembly.</summary>
public sealed class SecondModuleHandler(SecondModuleRecorder recorder) : IMessageHandler<SecondModuleEvent>
{
    public ValueTask HandleAsync(SecondModuleEvent message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Handled.Enqueue((message.Id, DateTimeOffset.UtcNow));
        return ValueTask.CompletedTask;
    }
}
