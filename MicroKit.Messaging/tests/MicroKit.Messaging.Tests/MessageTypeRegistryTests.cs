using FluentAssertions;
using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Core;

namespace MicroKit.Messaging.Tests;

public class MessageTypeRegistryTests
{
    private readonly MessageTypeRegistry _registry = new();

    [Fact]
    public void Register_And_Resolve_ShouldReturnRegisteredType()
    {
        _registry.Register("MyApp.OrderCreated", typeof(EventEnvelope<string>));

        var result = _registry.Resolve("MyApp.OrderCreated");

        result.Should().Be(typeof(EventEnvelope<string>));
    }

    [Fact]
    public void Resolve_UnknownType_ShouldReturnNull()
    {
        var result = _registry.Resolve("Unknown.Type");
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        _registry.Register("MyApp.OrderCreated", typeof(EventEnvelope<string>));

        _registry.Resolve("myapp.ordercreated").Should().Be(typeof(EventEnvelope<string>));
        _registry.Resolve("MYAPP.ORDERCREATED").Should().Be(typeof(EventEnvelope<string>));
    }

    [Fact]
    public void Register_Override_ShouldReplaceExistingType()
    {
        _registry.Register("MyApp.OrderCreated", typeof(EventEnvelope<string>));
        _registry.Register("MyApp.OrderCreated", typeof(EventEnvelope<int>));

        _registry.Resolve("MyApp.OrderCreated").Should().Be(typeof(EventEnvelope<int>));
    }

    [Fact]
    public void Register_NullMessageType_ShouldThrow()
    {
        var act = () => _registry.Register(null!, typeof(string));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Register_EmptyMessageType_ShouldThrow()
    {
        var act = () => _registry.Register("", typeof(string));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Register_NullEnvelopeType_ShouldThrow()
    {
        var act = () => _registry.Register("some.type", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Register_MultipleTypes_ShouldAllBeResolvable()
    {
        _registry.Register("TypeA", typeof(EventEnvelope<string>));
        _registry.Register("TypeB", typeof(EventEnvelope<int>));
        _registry.Register("TypeC", typeof(EventEnvelope<bool>));

        _registry.Resolve("TypeA").Should().Be(typeof(EventEnvelope<string>));
        _registry.Resolve("TypeB").Should().Be(typeof(EventEnvelope<int>));
        _registry.Resolve("TypeC").Should().Be(typeof(EventEnvelope<bool>));
    }

    [Fact]
    public void MessageTypeRegistry_ShouldImplement_IMessageTypeRegistry()
    {
        _registry.Should().BeAssignableTo<IMessageTypeRegistry>();
    }

    [Fact]
    public void Register_IsConcurrentlySafe()
    {
        // Register 100 types concurrently — should not throw
        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => _registry.Register($"Type{i}", typeof(EventEnvelope<string>))))
            .ToArray();

        var act = async () => await Task.WhenAll(tasks);
        act.Should().NotThrowAsync();
    }
}
