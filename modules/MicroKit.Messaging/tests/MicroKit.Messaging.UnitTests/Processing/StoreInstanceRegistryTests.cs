using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Primitives;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// The delivery path's lookup (#191 D4, A2): a source that cannot map a delivery maps it to nothing — the
/// delivery is not drained here, logged as a mapping failure (1416) — and never fails the pipeline.
/// </summary>
public sealed class StoreInstanceRegistryTests
{
    private const int SourceFailed = 1407;
    private const int MappingFailed = 1416;

    [Fact]
    public void ASourceThatAnswersTrueWithNoInstance_MapsNothing_AndIsLogged()
    {
        var log = new EventLog<StoreInstanceRegistry>();
        using var harness = new InstanceHarness(
            configure: s => s.AddKeyedSingleton<IMessageStoreInstanceSource>(InstanceHarness.Store, new Mapping(throws: false)),
            registryLogger: log);

        harness.Registry.Find(InstanceHarness.Store, Context()).ShouldBeNull("a source's broken promise maps nothing");
        log.Count(MappingFailed).ShouldBe(1);
    }

    [Fact]
    public void ASourceWhoseMappingThrows_MapsNothing_AndIsLoggedAsAMappingFailure()
    {
        var log = new EventLog<StoreInstanceRegistry>();
        using var harness = new InstanceHarness(
            configure: s => s.AddKeyedSingleton<IMessageStoreInstanceSource>(InstanceHarness.Store, new Mapping(throws: true)),
            registryLogger: log);

        harness.Registry.Find(InstanceHarness.Store, Context()).ShouldBeNull();
        log.Count(MappingFailed).ShouldBe(1, "a failed mapping is one delivery's, not the snapshot's");
        log.Count(SourceFailed).ShouldBe(0);
    }

    [Fact]
    public async Task ADeliverysWaitForAFirstSnapshot_IsBoundedByOnePollingInterval()
    {
        using var harness = new InstanceHarness();
        var time = new FakeTimeProvider();
        var registry = new StoreInstanceRegistry(
            harness.Composition, harness.Provider, NullLogger<StoreInstanceRegistry>.Instance, harness.OutboxOptions, time);

        var wait = registry.WaitForFirstRoundAsync(InstanceHarness.Store).AsTask();
        wait.IsCompleted.ShouldBeFalse("no reconciler has applied a snapshot");

        time.Advance(harness.OutboxOptions.PollingInterval);
        await wait.WaitAsync(TimeSpan.FromSeconds(5));   // a reconciler that never runs holds no delivery longer
    }

    [Fact]
    public async Task OnceTheBoundHasPassed_LaterDeliveriesDoNotWait()
    {
        using var harness = new InstanceHarness();
        var time = new FakeTimeProvider();
        var registry = new StoreInstanceRegistry(
            harness.Composition, harness.Provider, NullLogger<StoreInstanceRegistry>.Instance, harness.OutboxOptions, time);
        var first = registry.WaitForFirstRoundAsync(InstanceHarness.Store).AsTask();
        time.Advance(harness.OutboxOptions.PollingInterval);
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        registry.WaitForFirstRoundAsync(InstanceHarness.Store).IsCompleted
            .ShouldBeTrue("the bound is the store's, not each delivery's: a catalog still down costs every delivery nothing more");
    }

    private static MessageContext Context() => new()
    {
        MessageId = MessageId.New(),
        ContractName = "tests.registry",
        ContractVersion = 1,
        Source = "/tests",
        OccurredOnUtc = DateTimeOffset.UnixEpoch,
    };

    /// <summary>A source whose mapping throws, or answers true with no instance.</summary>
    private sealed class Mapping(bool throws) : IMessageStoreInstanceSource
    {
        public IReadOnlyCollection<MessageStoreInstance> GetInstances() => [];

        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);

        public bool TryGetInstance(MessageContext context, [NotNullWhen(true)] out MessageStoreInstance? instance)
        {
            instance = null;
            return throws ? throw new InvalidOperationException("The tenant catalog is down.") : true;
        }
    }
}
