using System.Diagnostics.CodeAnalysis;

namespace MicroKit.MediatR.Events;

/// <summary>
/// Registration-time metadata about which domain event types have a
/// <see cref="DomainEventNotification{TEvent}"/> mapping. Built by the assembly scan in
/// <c>AddMicroKitMediatR</c> and registered as a singleton.
/// </summary>
/// <remarks>
/// <para>
/// Exists so <see cref="DomainEventDispatcher"/> can answer "would this event have produced a
/// notification?" without calling <see cref="IDomainEventNotificationFactory"/>, which would
/// <b>construct</b> one — a call and an allocation per event, on every batch, for a consumer who
/// legitimately has no sink and no notifications. <see cref="IsEmpty"/> is what makes that path
/// free (ADR-MEDIATR-015).
/// </para>
/// <para>
/// The map is the one the scan already builds and previously discarded; nothing extra is computed
/// to populate it.
/// </para>
/// </remarks>
internal sealed class DomainEventNotificationCatalog(IReadOnlyDictionary<Type, Type> map)
{
    /// <summary>True when no notification type was discovered in any scanned assembly.</summary>
    internal bool IsEmpty => map.Count == 0;

    /// <summary>
    /// Gets the concrete <see cref="DomainEventNotification{TEvent}"/> subclass registered for
    /// <paramref name="eventType"/>, if any.
    /// </summary>
    internal bool TryGetNotificationType(
        Type eventType,
        [MaybeNullWhen(false)] out Type notificationType)
        => map.TryGetValue(eventType, out notificationType);
}
