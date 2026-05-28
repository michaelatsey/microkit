namespace MicroKit.MediatR;

/// <summary>
/// Marks a domain fact that has already happened.
/// Implement this on domain event records to make them dispatchable via
/// <see cref="IDomainEventNotification{TEvent}"/> through the MediatR pipeline.
/// </summary>
/// <example>
/// <code>
/// public sealed record UserRegisteredEvent(Guid UserId, string Email, DateTimeOffset RegisteredAt) : IEvent;
/// </code>
/// </example>
public interface IEvent;
