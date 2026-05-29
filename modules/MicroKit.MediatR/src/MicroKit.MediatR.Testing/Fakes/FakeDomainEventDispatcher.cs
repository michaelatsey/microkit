namespace MicroKit.MediatR.Testing;

/// <summary>
/// An in-memory implementation of <see cref="IDomainEventDispatcher"/> that records every published
/// event. Inject into command handlers under test in place of the real dispatcher.
/// </summary>
/// <remarks>
/// Not thread-safe. Designed for single-threaded unit test execution.
/// </remarks>
/// <example>
/// <code>
/// var dispatcher = new FakeDomainEventDispatcher();
/// var harness = new CommandHandlerTestHarness&lt;CreateOrderCommand, Result&lt;OrderId&gt;&gt;(
///     dispatcher => new CreateOrderHandler(mockRepo, dispatcher));
///
/// await harness.SendAsync(new CreateOrderCommand(...));
/// harness.AssertEventPublished&lt;OrderCreatedEvent&gt;();
/// </code>
/// </example>
public sealed class FakeDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly List<IEvent> _events = [];

    /// <summary>Gets all domain events published since the last <see cref="Reset"/> (or construction).</summary>
    public IReadOnlyList<IEvent> PublishedEvents => _events;

    /// <inheritdoc/>
    public ValueTask PublishAsync(IEvent domainEvent, CancellationToken ct = default)
    {
        _events.Add(domainEvent);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Asserts that at least one event of type <typeparamref name="TEvent"/> was published.
    /// Fails the test with a descriptive Shouldly message if none was published.
    /// </summary>
    /// <typeparam name="TEvent">The expected domain event type.</typeparam>
    public void AssertEventPublished<TEvent>() where TEvent : IEvent =>
        _events.OfType<TEvent>().ShouldNotBeEmpty(
            $"Expected an event of type '{typeof(TEvent).Name}' to be published, " +
            $"but found: [{string.Join(", ", _events.Select(e => e.GetType().Name))}]");

    /// <summary>
    /// Asserts that no events of any type were published.
    /// Fails the test with a descriptive Shouldly message if any were published.
    /// </summary>
    public void AssertNoEventsPublished() =>
        _events.ShouldBeEmpty(
            $"Expected no events to be published, " +
            $"but found: [{string.Join(", ", _events.Select(e => e.GetType().Name))}]");

    /// <summary>
    /// Returns the single published event of type <typeparamref name="TEvent"/>.
    /// Fails the test if zero or more than one such event was published.
    /// </summary>
    /// <typeparam name="TEvent">The expected domain event type.</typeparam>
    public TEvent GetSinglePublishedEvent<TEvent>() where TEvent : IEvent
    {
        var events = _events.OfType<TEvent>().ToList();
        events.Count.ShouldBe(1,
            $"Expected exactly one event of type '{typeof(TEvent).Name}', but found {events.Count}. " +
            $"All published: [{string.Join(", ", _events.Select(e => e.GetType().Name))}]");
        return events[0];
    }

    /// <summary>Clears all recorded events. Use between test cases that share a harness instance.</summary>
    public void Reset() => _events.Clear();
}
