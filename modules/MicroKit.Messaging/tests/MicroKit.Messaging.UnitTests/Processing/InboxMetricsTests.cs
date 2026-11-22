using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// <c>microkit.inbox.tenants.backoff</c> (#126): per known store, the tenants not recovered on its inbox —
/// from a down verdict until a success, or until a ceiling plus MaxPollingInterval after the back-off ended
/// with no new attempt — zero included, so an alert resolves.
/// </summary>
public sealed class InboxMetricsTests
{
    private const string Tenants = "microkit.inbox.tenants.backoff";

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly MessageStoreKey S1 = new("tests:inbox-metrics-1");
    private static readonly MessageStoreKey S2 = new("tests:inbox-metrics-2");
    private static readonly InboxProcessorOptions Options = new();
    private static readonly TimeSpan HalfOpen = Options.DependencyUnavailableBackoff + Options.MaxPollingInterval;

    [Fact]
    public void TwoTenantsDownInOneStore_AreCountedForIt_AndZeroForTheOther_UntilASuccessOrTheBound()
    {
        var clock = new FakeTimeProvider(Now);
        var backoff = new InboxTenantBackoff(Options);
        using var metrics = new InboxMetrics(backoff, clock, Options);
        using var recorder = new MeterRecorder(metrics.Meter);

        var s1 = backoff.For(S1);
        s1.OnDown("tenant-a", Now, out _);
        s1.OnDown("tenant-b", Now, out var untilB);
        backoff.For(S2);

        Observe(recorder).ShouldBe([$"{S1.Value}=2", $"{S2.Value}=0"]);

        // Both back-offs over, no attempt since: half-open, still counted.
        clock.Advance(TimeSpan.FromMinutes(1));
        s1.BlockedAt(clock.GetUtcNow()).ShouldBeEmpty("control: no back-off is in force any more");
        Observe(recorder).ShouldBe([$"{S1.Value}=2", $"{S2.Value}=0"]);

        s1.Succeeded("tenant-a");
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

    private static List<string> Observe(MeterRecorder recorder)
        => [.. recorder.Observe(Tenants).Select(r => $"{r.Tag("store.key")}={r.Value}").Order(StringComparer.Ordinal)];
}
