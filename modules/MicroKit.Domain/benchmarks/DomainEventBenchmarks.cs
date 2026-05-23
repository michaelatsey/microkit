using BenchmarkDotNet.Attributes;
using MicroKit.Domain.Events;
using MicroKit.Domain.ValueObjects.Common;
using MicroKit.Domain.Identifiers;

namespace MicroKit.Domain.Benchmarks;

/// <summary>
/// Benchmarks for domain event creation, equality, and collection operations.
/// Demonstrates the performance characteristics of sealed record events.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class DomainEventBenchmarks
{
    private readonly BenchmarkOrderId _orderId;
    private readonly BenchmarkCustomerId _customerId;
    private readonly Money _amount;
    private readonly DateTimeOffset _timestamp;

    private readonly OrderPlacedEvent _event1;
    private readonly OrderPlacedEvent _event2;
    private readonly OrderPlacedEvent _eventDifferent;

    private readonly List<IDomainEvent> _eventCollection;

    public DomainEventBenchmarks()
    {
        _orderId = BenchmarkOrderId.New();
        _customerId = BenchmarkCustomerId.New();
        _amount = new Money(100.50m, "USD");
        _timestamp = DateTimeOffset.UtcNow;

        _event1 = new OrderPlacedEvent(_orderId, _customerId, _amount);
        _event2 = new OrderPlacedEvent(_orderId, _customerId, _amount);
        _eventDifferent = new OrderPlacedEvent(BenchmarkOrderId.New(), _customerId, _amount);

        _eventCollection = new List<IDomainEvent>();
        for (int i = 0; i < 100; i++)
        {
            _eventCollection.Add(new OrderPlacedEvent(BenchmarkOrderId.New(), _customerId, _amount));
        }
    }

    [Benchmark(Description = "Create Order Placed Event")]
    public OrderPlacedEvent CreateOrderPlacedEvent()
    {
        return new OrderPlacedEvent(_orderId, _customerId, _amount);
    }

    [Benchmark(Description = "Create Customer Registered Event")]
    public CustomerRegisteredEvent CreateCustomerRegisteredEvent()
    {
        return new CustomerRegisteredEvent(_customerId, new Email("test@example.com"));
    }

    [Benchmark(Description = "Event Equality (True)")]
    public bool EventEqualityTrue()
    {
        return _event1.Equals(_event2);
    }

    [Benchmark(Description = "Event Equality (False)")]
    public bool EventEqualityFalse()
    {
        return _event1.Equals(_eventDifferent);
    }

    [Benchmark(Description = "Event Hash Code")]
    public int EventHashCode()
    {
        return _event1.GetHashCode();
    }

    [Benchmark(Description = "Event Collection Add")]
    public List<IDomainEvent> EventCollectionAdd()
    {
        var events = new List<IDomainEvent>();

        for (int i = 0; i < 10; i++)
        {
            events.Add(new OrderPlacedEvent(BenchmarkOrderId.New(), _customerId, _amount));
        }

        return events;
    }

    [Benchmark(Description = "Event Collection ToArray")]
    public IDomainEvent[] EventCollectionToArray()
    {
        return _eventCollection.ToArray();
    }

    [Benchmark(Description = "Event Collection Enumeration")]
    public int CountEvents()
    {
        int count = 0;
        foreach (var evt in _eventCollection)
        {
            if (evt is OrderPlacedEvent)
                count++;
        }
        return count;
    }

    [Benchmark(Description = "Event Pattern Matching")]
    public int PatternMatchEvents()
    {
        int count = 0;
        foreach (var evt in _eventCollection)
        {
            count += evt switch
            {
                OrderPlacedEvent => 1,
                CustomerRegisteredEvent => 2,
                _ => 0
            };
        }
        return count;
    }

    [Benchmark(Description = "Event Serialization ToString")]
    public string[] SerializeEventsToString()
    {
        return _eventCollection.Select(e => e.ToString() ?? string.Empty).ToArray();
    }
}

