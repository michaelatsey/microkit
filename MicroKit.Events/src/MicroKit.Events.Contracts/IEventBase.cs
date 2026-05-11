namespace MicroKit.Events.Contracts;

/// <summary>Base contract shared by all event types.</summary>
public interface IEventBase
{
    /// <summary>Gets the unique identifier for this event.</summary>
    Guid Id { get; }

    /// <summary>Gets the UTC timestamp when the event occurred.</summary>
    DateTimeOffset OccurredOnUtc { get; }
}
