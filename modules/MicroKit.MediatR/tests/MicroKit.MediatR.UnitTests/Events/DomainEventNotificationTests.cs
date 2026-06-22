using MicroKit.MediatR;
using MicroKit.Domain.Events;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.UnitTests.Events;

/// <summary>
/// Verifies the <see cref="DomainEventNotification{TEvent}"/> abstract base class:
/// the constructor stores the event, the <c>DomainEvent</c> property returns the same
/// reference, and the derived class correctly implements <see cref="IDomainEventNotification{TEvent}"/>.
/// </summary>
public sealed class DomainEventNotificationTests
{
    [Fact]
    public void Constructor_StoresDomainEvent()
    {
        var evt = new UserRegisteredEvent(Guid.NewGuid(), "user@example.com");
        var notification = new UserRegisteredNotification(evt);

        notification.DomainEvent.ShouldBe(evt);
    }

    [Fact]
    public void DomainEventProperty_ReturnsSameReferencePassedToConstructor()
    {
        var evt = new UserRegisteredEvent(Guid.NewGuid(), "user@example.com");
        var notificationA = new UserRegisteredNotification(evt);
        var notificationB = new UserRegisteredNotification(evt);

        notificationA.DomainEvent.ShouldBeSameAs(notificationB.DomainEvent,
            "DomainEvent must be the exact object passed to the constructor, not a copy");
    }

    [Fact]
    public void Notification_ImplementsIDomainEventNotification()
    {
        var evt = new UserRegisteredEvent(Guid.NewGuid(), "user@example.com");
        var notification = new UserRegisteredNotification(evt);

        notification.ShouldBeAssignableTo<IDomainEventNotification<UserRegisteredEvent>>();
        ((IDomainEventNotification<UserRegisteredEvent>)notification).DomainEvent.ShouldBe(evt);
    }

    // ── Private fixtures ──────────────────────────────────────────────────

    private sealed record UserRegisteredEvent(Guid UserId, string Email) : DomainEvent;

    private sealed class UserRegisteredNotification(UserRegisteredEvent domainEvent)
        : DomainEventNotification<UserRegisteredEvent>(domainEvent);
}
