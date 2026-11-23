using System.Diagnostics.CodeAnalysis;
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
