namespace MicroKit.Messaging.UnitTests.DI;

using MicroKit.Messaging.Outbox;
using MicroKit.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Where the <see cref="IMessageSerializer"/> default comes from, and for which compositions.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because the gap it guards has already shipped once.</b> The default used to live
/// on <c>AddInProcessTransport()</c>; when that method was deleted (ADR-MSG-019) the change was
/// planned on the belief that the remaining <c>TryAdd</c>s covered it. They did not — a host
/// composing plain outbox, or outbox plus transport, was left holding a registered
/// <c>InboxProcessor</c> and <c>OutboxMessageFactory</c> it could not activate. That surfaces at the
/// first worker tick rather than at composition, which is the failure shape this module treats as
/// blocking.
/// </para>
/// <para>
/// <b>The property is structural and is asserted as such.</b> <see cref="MessagingBuilder"/>'s
/// constructor is <see langword="internal"/>, so no builder extension can run without
/// <c>AddMicroKitMessaging()</c> having run first — and that method registers the two types needing
/// a serializer, so it is the only correct owner of the default. These tests pin the consequence
/// for each composition rather than restating the argument.
/// </para>
/// <para>
/// <b><c>OutboxMessageFactory</c> is resolved, not merely counted.</b> A descriptor assertion proves
/// a registration exists; activating the one singleton that takes <see cref="IMessageSerializer"/>
/// through its constructor proves the default is actually reachable by a real consumer.
/// <c>InboxProcessor</c> is deliberately not resolved here: it also requires the inbox stores, which
/// come from <c>AddEfCoreOutbox()</c>, so its failure would not isolate the serializer.
/// </para>
/// <para>
/// The notification-only composition lives in <c>MicroKit.Messaging.MediatR.UnitTests</c> — this
/// project does not reference the glue.
/// </para>
/// </remarks>
public sealed class MessagingSerializerDefaultTests
{
    private static ServiceProvider Build(Action<MessagingBuilder> compose)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        compose(services.AddMicroKitMessaging());

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public static TheoryData<string, Action<MessagingBuilder>> Compositions => new()
    {
        { "outbox only", _ => { } },
        { "outbox + transport", b => b.AddTransportDispatcher() },
        { "outbox + publishing", b => b.AddIntegrationEventPublishing() },
        { "outbox + consumption", b => b.AddIntegrationEventConsumption() },
    };

    [Theory]
    [MemberData(nameof(Compositions))]
    public void EveryComposition_ResolvesTheSerializerDefault(
        string composition, Action<MessagingBuilder> compose)
    {
        using var provider = Build(compose);

        provider.GetService<IMessageSerializer>()
            .ShouldBeOfType<SystemTextJsonMessageSerializer>(
                $"'{composition}' must get the default from AddMicroKitMessaging()");
    }

    [Theory]
    [MemberData(nameof(Compositions))]
    public void EveryComposition_ActivatesOutboxMessageFactory(
        string composition, Action<MessagingBuilder> compose)
    {
        using var provider = Build(compose);

        // The activation is the point: it takes IMessageSerializer by constructor, so this fails if
        // the default is registered under the wrong lifetime or not at all.
        provider.GetService<OutboxMessageFactory>()
            .ShouldNotBeNull($"'{composition}' must be able to activate the factory");
    }

    [Fact]
    public void AddMicroKitMessaging_IsWhatRegistersTheDefault_NotABuilderMethod()
    {
        // Asserted on the bare call with no builder method at all. If the default ever migrates
        // back onto an optional builder extension, this is the test that says so.
        var services = new ServiceCollection();

        services.AddMicroKitMessaging();

        services.Where(d => d.ServiceType == typeof(IMessageSerializer))
            .ShouldHaveSingleItem()
            .ImplementationType.ShouldBe(typeof(SystemTextJsonMessageSerializer));
    }

    [Fact]
    public void AddIntegrationEventPublishing_AddsNoSerializerOfItsOwn()
    {
        // A DELTA, not an absence: AddMicroKitMessaging() has already supplied one, so asserting
        // "no serializer in the collection" would fail for the wrong reason. This method used to
        // TryAdd a second, which is unreachable code that reads like a safeguard (ADR-MSG-019).
        var services = new ServiceCollection();
        var builder = services.AddMicroKitMessaging();
        var before = services.Count(d => d.ServiceType == typeof(IMessageSerializer));

        builder.AddIntegrationEventPublishing();

        services.Count(d => d.ServiceType == typeof(IMessageSerializer)).ShouldBe(before);
    }

    [Fact]
    public void AHostSerializer_RegisteredBeforeAddMicroKitMessaging_Wins()
    {
        // The override order the XML docs on AddMicroKitMessaging() promise. TryAdd means FIRST
        // registration wins, so a host that registers afterwards loses silently — which is why the
        // docs say BEFORE and why that word is worth a test.
        var services = new ServiceCollection();
        services.AddSingleton<IMessageSerializer, RecordingSerializer>();

        services.AddMicroKitMessaging();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageSerializer>().ShouldBeOfType<RecordingSerializer>();
    }

    [Fact]
    public void AHostSerializer_RegisteredAfterAddMicroKitMessaging_DoesNotWin()
    {
        // The other half, and the reason the docs had to be corrected: the same two lines in the
        // other order silently keep the default. Pinned so nobody "fixes" the docs back.
        var services = new ServiceCollection();

        services.AddMicroKitMessaging();
        services.TryAddSingleton<IMessageSerializer, RecordingSerializer>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IMessageSerializer>()
            .ShouldBeOfType<SystemTextJsonMessageSerializer>();
    }

    private sealed class RecordingSerializer : IMessageSerializer
    {
        public string Serialize(object payload) => string.Empty;

        public object? Deserialize(string payload, string eventType) => null;
    }
}
