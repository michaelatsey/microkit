using FluentAssertions;
using MicroKit.Events.Contracts;

namespace MicroKit.Events.Tests;

/// <summary>
/// Contract tests: ensures IEventBase, IEvent, and IIntegrationEvent share the correct hierarchy.
/// </summary>
public class IEventBaseContractTests
{
    [Fact]
    public void IEvent_ShouldExtend_IEventBase()
    {
        typeof(IEventBase).IsAssignableFrom(typeof(IEvent)).Should().BeTrue();
    }

    [Fact]
    public void IIntegrationEvent_ShouldExtend_IEventBase()
    {
        typeof(IEventBase).IsAssignableFrom(typeof(IIntegrationEvent)).Should().BeTrue();
    }

    [Fact]
    public void IEvent_ShouldHave_Metadata_AsIReadOnlyDictionary()
    {
        var prop = typeof(IEvent).GetProperty(nameof(IEvent.Metadata));
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(IReadOnlyDictionary<string, string>));
    }

    [Fact]
    public void IEvent_ShouldHave_MessageType_Property()
    {
        typeof(IEvent).GetProperty(nameof(IEvent.MessageType)).Should().NotBeNull();
    }

    [Fact]
    public void IIntegrationEvent_ShouldHave_MessageType_Property()
    {
        typeof(IIntegrationEvent).GetProperty(nameof(IIntegrationEvent.MessageType)).Should().NotBeNull();
    }

    [Fact]
    public void IIntegrationEvent_ShouldHave_CorrelationId_Property()
    {
        typeof(IIntegrationEvent).GetProperty(nameof(IIntegrationEvent.CorrelationId)).Should().NotBeNull();
    }

    [Fact]
    public void IEventBase_ShouldHave_Id_And_OccurredOnUtc()
    {
        typeof(IEventBase).GetProperty(nameof(IEventBase.Id)).Should().NotBeNull();
        typeof(IEventBase).GetProperty(nameof(IEventBase.OccurredOnUtc)).Should().NotBeNull();
    }
}
