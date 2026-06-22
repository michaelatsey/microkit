namespace MicroKit.MediatR.Events;

/// <summary>
/// Compatibility alias for <see cref="IDomainEventNotificationFactory"/>.
/// </summary>
/// <remarks>
/// New code should inject <see cref="IDomainEventNotificationFactory"/>. This interface remains
/// for preview compatibility and will be removed in the next major version.
/// </remarks>
[Obsolete("Use IDomainEventNotificationFactory.")]
public interface INotificationFactory : IDomainEventNotificationFactory;
