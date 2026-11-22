using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// <c>microkit.inbox.tenants.backoff</c> (#126): per known store, the tenants whose back-off is in
/// force on its inbox — zero included, so an alert resolves — never the entries waiting to be pruned.
/// </summary>
public sealed class InboxMetricsTests
{
    private const string Tenants = "microkit.inbox.tenants.backoff";

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly MessageStoreKey S1 = new("tests:inbox-metrics-1");
    private static readonly MessageStoreKey S2 = new("tests:inbox-metrics-2");

    [Fact]
    public void TwoTenantsInBackOffInOneStore_AreCountedForIt_AndZeroForTheOther_UntilTheirBackOffEnds()
    {
        var clock = new FakeTimeProvider(Now);
        var backoff = new InboxTenantBackoff(new InboxProcessorOptions());
        using var metrics = new InboxMetrics(backoff, clock);
        using var recorder = new MeterRecorder(metrics.Meter);

        var s1 = backoff.For(S1);
        s1.OnDown("tenant-a", Now, out _);
        s1.OnDown("tenant-b", Now, out _);
        backoff.For(S2);

        Observe(recorder).ShouldBe([$"{S1.Value}=2", $"{S2.Value}=0"]);

        // Both back-offs over, neither entry pruned yet: one waits a whole ceiling past its end.
        clock.Advance(TimeSpan.FromMinutes(1));
        s1.IsEmpty.ShouldBeFalse("control: both entries are still there");
        Observe(recorder).ShouldBe([$"{S1.Value}=0", $"{S2.Value}=0"], "a tenant counts while its back-off is in force");
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
