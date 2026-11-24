using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// <c>microkit.inbox.tenants.backoff</c> (#126): per known store, the tenants not recovered on its inbox —
/// from a down verdict until a success, or until a ceiling plus MaxPollingInterval after the back-off ended
/// with no new attempt — zero included, so an alert resolves. Since #210 a tenant counts while any of its
/// (consumer, tenant) pairs has not recovered, and <c>microkit.inbox.consumers.backoff</c> counts the consumers
/// in consumer-wide back-off by the same rule (V11).
/// </summary>
public sealed class InboxMetricsTests
{
    private const string Tenants = "microkit.inbox.tenants.backoff";
    private const string Consumer = "Tests.MetricsConsumer, Tests";
    private const string Consumers = "microkit.inbox.consumers.backoff";
    private const string X = Consumer;
    private const string Y = "Tests.OtherMetricsConsumer, Tests";

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly MessageStoreKey S1 = new("tests:inbox-metrics-1");
    private static readonly MessageStoreKey S2 = new("tests:inbox-metrics-2");
    private static readonly InboxProcessorOptions Options = new();
    private static readonly TimeSpan HalfOpen = Options.DependencyUnavailableBackoff + Options.MaxPollingInterval;

    [Fact]
    public void TwoTenantsDownInOneStore_AreCountedForIt_AndZeroForTheOther_UntilASuccessOrTheBound()
    {
        var clock = new FakeTimeProvider(Now);
        var backoff = new InboxBackoffGroup(Options);
        using var metrics = new InboxMetrics(backoff, clock, Options);
        using var recorder = new MeterRecorder(metrics.Meter);

        var s1 = backoff.For(S1);
        s1.OnDown(Consumer, "tenant-a", Now, out _);
        s1.OnDown(Consumer, "tenant-b", Now, out var untilB);
        backoff.For(S2);

        Observe(recorder).ShouldBe([$"{S1.Value}=2", $"{S2.Value}=0"]);

        // Both back-offs over, no attempt since: half-open, still counted.
        clock.Advance(TimeSpan.FromMinutes(1));
        s1.ExclusionsAt(clock.GetUtcNow()).ShouldBeSameAs(InboxClaimExclusions.None, "control: no back-off is in force any more");
        Observe(recorder).ShouldBe([$"{S1.Value}=2", $"{S2.Value}=0"]);

        s1.Succeeded(Consumer, "tenant-a");
        Observe(recorder).ShouldBe([$"{S1.Value}=1", $"{S2.Value}=0"], "a success clears its tenant at once");

        clock.SetUtcNow(untilB + HalfOpen - TimeSpan.FromTicks(1));
        Observe(recorder).ShouldBe([$"{S1.Value}=1", $"{S2.Value}=0"], "still within the bound");
        clock.SetUtcNow(untilB + HalfOpen);
        Observe(recorder).ShouldBe([$"{S1.Value}=0", $"{S2.Value}=0"], "nothing left to retry: it drops out");
    }

    [Fact]
    public void AnInstanceBuiltWithNoBackOff_ReportsNoStore()
    {
        using var metrics = new InboxMetrics();
        using var recorder = new MeterRecorder(metrics.Meter);

        recorder.Published.ShouldContain(Tenants);
        recorder.Observe(Tenants).ShouldBeEmpty();
    }

    /// <summary>
    /// V11: X's dependency down for A while Y keeps succeeding for A — the tenants counter stays above 0
    /// through X's outage, where on main each of Y's successes cleared A's entry and dropped it to 0 (#210).
    /// </summary>
    [Fact]
    public void TheTenantsCounter_StaysAboveZeroThroughXsOutage_WhileYSucceedsForTheSameTenant()
    {
        var clock = new FakeTimeProvider(Now);
        var group = new InboxBackoffGroup(Options);
        using var metrics = new InboxMetrics(group, clock, Options);
        using var recorder = new MeterRecorder(metrics.Meter);
        var store = group.For(S1);

        store.OnDown(X, "A", clock.GetUtcNow(), out var until);
        for (var outage = 0; outage < 5; outage++)
        {
            store.Succeeded(Y, "A");
            Observe(recorder).ShouldBe([$"{S1.Value}=1"], $"step {outage}: Y's success for A leaves X's pair");
            clock.SetUtcNow(until);
            store.OnDown(X, "A", until, out until).ShouldBe(InboxBackoff.OutageScope.Tenant);
        }

        store.Succeeded(X, "A");
        Observe(recorder).ShouldBe([$"{S1.Value}=0"], "X's own success clears it");
    }

    [Fact]
    public void ATenantWithTwoPairs_CountsOnce()
    {
        var group = new InboxBackoffGroup(Options);
        using var metrics = new InboxMetrics(group, new FakeTimeProvider(Now), Options);
        using var recorder = new MeterRecorder(metrics.Meter);
        var store = group.For(S1);

        store.OnDown(X, "A", Now, out _);
        store.OnDown(Y, "A", Now, out _);

        Observe(recorder).ShouldBe([$"{S1.Value}=1"], "one tenant, two pairs");
        Observe(recorder, Consumers).ShouldBe([$"{S1.Value}=0"], "a tenant's database down holds no consumer");
    }

    /// <summary>V11: the consumers counter reads 1 during X's consumer-wide back-off and 0 after X's success.</summary>
    [Fact]
    public void TheConsumersCounter_ReadsOneDuringXsConsumerBackOff_AndZeroAfterXsSuccess_ZeroForAnotherStore()
    {
        var clock = new FakeTimeProvider(Now);
        var group = new InboxBackoffGroup(Options);
        using var metrics = new InboxMetrics(group, clock, Options);
        using var recorder = new MeterRecorder(metrics.Meter);
        var store = group.For(S1);
        group.For(S2);

        store.OnDown(X, "A", Now, out _);
        store.Succeeded(Y, "Z");
        store.OnDown(X, "B", Now, out var until).ShouldBe(InboxBackoff.OutageScope.Consumer);

        Observe(recorder, Consumers).ShouldBe([$"{S1.Value}=1", $"{S2.Value}=0"]);
        clock.SetUtcNow(until + HalfOpen - TimeSpan.FromTicks(1));
        Observe(recorder, Consumers).ShouldBe([$"{S1.Value}=1", $"{S2.Value}=0"], "half-open, still counted");

        store.Succeeded(X, null);
        Observe(recorder, Consumers).ShouldBe([$"{S1.Value}=0", $"{S2.Value}=0"], "X's success clears it, zero still reported");
        recorder.Published.ShouldContain(Consumers);
    }

    private static List<string> Observe(MeterRecorder recorder, string instrument = Tenants)
        => [.. recorder.Observe(instrument).Select(r => $"{r.Tag("store.key")}={r.Value}").Order(StringComparer.Ordinal)];
}
