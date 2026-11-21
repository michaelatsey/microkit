namespace MicroKit.Messaging.MediatR.UnitTests;

/// <summary>
/// The sink's part of domain-event dispatch (#188 D8): each event with a notification becomes one
/// local message, sent through <see cref="ILocalMessageSender"/>. Where it lands — a store's outbox
/// or the in-memory queue — is the sender's (core's LocalMessageSender and OutboxMessageFactory).
/// </summary>
/// <remarks>
/// A recording sender, not a mock: every "sent nothing" assertion reads its list, which a
/// <c>DidNotReceive()</c> on a method the subject might not call could not falsify.
/// </remarks>
public sealed class DomainEventNotificationSinkTests
{
    private readonly IDomainEventNotificationFactory _factory = Substitute.For<IDomainEventNotificationFactory>();
    private readonly RecordingLocalSender _sender = new();

    private DomainEventNotificationSink Sut => new(_factory, _sender);

    [Fact]
    public async Task ReceiveAsync_WhenNoEventMapsToANotification_SendsNothing()
    {
        _factory.Create(Arg.Any<IDomainEvent>()).Returns((IDomainEventNotification<IDomainEvent>?)null);

        await Sut.ReceiveAsync([new OrderPlaced(Guid.NewGuid(), DateTimeOffset.UnixEpoch)]);

        _sender.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReceiveAsync_SendsOnlyTheMappedEvents_InDrainOrder_EachUnderItsEventsIdentityAndTime()
    {
        var first = new OrderPlaced(Guid.NewGuid(), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var unmapped = new OrderShipped(Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        var second = new OrderPlaced(Guid.NewGuid(), new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        _factory.Create(Arg.Any<IDomainEvent>())
            .Returns(call => call.Arg<IDomainEvent>() is OrderPlaced placed ? new OrderPlacedNotification(placed) : null);

        await Sut.ReceiveAsync([first, unmapped, second]);

        _sender.Sent.Count.ShouldBe(2);
        _sender.Sent[0].Message.ShouldBeOfType<OrderPlacedNotification>().DomainEvent.ShouldBe(first);
        // The event's id is every consumer's inbox key: a redelivered notification is recognised.
        _sender.Sent[0].Id.ShouldBe(MessageId.From(first.EventId));
        _sender.Sent[0].OccurredOnUtc.ShouldBe(first.OccurredAt);
        _sender.Sent[1].Id.ShouldBe(MessageId.From(second.EventId));
        _sender.Sent[1].OccurredOnUtc.ShouldBe(second.OccurredAt);
    }
}
