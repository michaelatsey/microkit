namespace MicroKit.Messaging.UnitTests.Processing;

using System.Runtime.CompilerServices;
using static MicroKit.Messaging.Processing.TenantBackoff;

/// <summary>
/// The tenants in back-off on one shared resource (#124): the curve, the half-open probe and its two
/// additions (A1: a failed probe blocks its own tenant; P2: a known tenant counts as a probe only while
/// the resource is suspected), and the bound (V8).
/// </summary>
public sealed class TenantBackoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Initial = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(5);

    private static TenantBackoff New(StrongBox<int>? loud = null) => new(Initial, Ceiling, loud);

    [Fact]
    public void BlockedAt_ListsTheTenantsWhoseBackOffIsInForce_AndIsTheSharedEmptyArrayWhenThereIsNone()
    {
        var backoff = New();
        backoff.BlockedAt(Now).ShouldBeSameAs(Array.Empty<string>(), "nothing in back-off: no allocation");

        backoff.OnDown("A", Now, out var untilA);
        backoff.Succeeded(null);
        backoff.OnDown("B", Now + TimeSpan.FromSeconds(2), out _);

        backoff.BlockedAt(Now).Order(StringComparer.Ordinal).ShouldBe(["A", "B"]);
        backoff.BlockedAt(untilA).ShouldBe(["B"], "A's back-off has ended: it is claimed again");
        backoff.BlockedAt(untilA + Initial).ShouldBeSameAs(Array.Empty<string>(), "both ended, entries kept for the curve");
    }

    [Fact]
    public void OnDown_FollowsTheEndpointCurve_DoublingToTheCeiling_AndStartingOverAfterAFullCeiling()
    {
        var backoff = New();
        var now = Now;
        var expected = Initial;

        for (var failure = 0; failure < 10; failure++)
        {
            backoff.OnDown("A", now, out var until);
            (until - now).ShouldBe(expected, $"failure {failure + 1}");
            now = until;
            expected = expected * 2 > Ceiling ? Ceiling : expected * 2;
        }

        // A whole ceiling past the end of the back-off without failing: a new outage, the base delay.
        now += Ceiling;
        backoff.OnDown("A", now, out var restarted);
        (restarted - now).ShouldBe(Initial);
    }

    [Fact]
    public void OnDown_ASecondTenantDownBeforeAnySuccess_IsTheProbeFailing_AndBlocksThatTenantToo()
    {
        var backoff = New();

        backoff.OnDown("A", Now, out _).ShouldBe(OutageScope.Tenant, "the first tenant down: its own, until proven shared");
        backoff.OnDown("B", Now, out _).ShouldBe(OutageScope.Shared, "a second one before any success: the probe failed");

        // A1: the failed probe's tenant is in back-off too, or it would head every probe that follows.
        backoff.IsBlocked("B", Now, out var until).ShouldBeTrue();
        until.ShouldBe(Now + Initial);
    }

    [Fact]
    public void OnDown_ASuccessInBetween_ClearsTheProbe()
    {
        var backoff = New();

        backoff.OnDown("A", Now, out _);
        backoff.Succeeded("C");

        backoff.OnDown("B", Now, out _).ShouldBe(OutageScope.Tenant, "a success proved the resource up");
        backoff.IsBlocked("A", Now, out _).ShouldBeTrue("another tenant's success leaves A's entry");
    }

    [Fact]
    public void OnDown_AKnownTenantFailingAgain_IsItsOwnCause_UnlessTheResourceIsSuspected()
    {
        var backoff = New();
        backoff.OnDown("A", Now, out _);
        backoff.Succeeded("C");
        var later = Now + Initial;

        // Not suspected: its own back-off ended and it failed again — never the resource's.
        backoff.OnDown("A", later, out _).ShouldBe(OutageScope.Tenant);
        backoff.OnDown("A", later, out _).ShouldBe(OutageScope.Tenant, "however often");

        // Suspected (P2): a known tenant counts as a probe like any other.
        backoff.MarkSuspected();
        backoff.OnDown("A", later, out _).ShouldBe(OutageScope.Tenant, "the first sets the probe");
        backoff.OnDown("A", later, out _).ShouldBe(OutageScope.Shared, "the second, before any success, trips");
    }

    [Fact]
    public void Succeeded_ClearsTheSuspicion()
    {
        var backoff = New();
        backoff.OnDown("A", Now, out _);
        backoff.OnDown("B", Now, out _).ShouldBe(OutageScope.Shared);

        backoff.Succeeded(null);

        backoff.OnDown("A", Now + Initial, out _).ShouldBe(OutageScope.Tenant, "known, not suspected: its own");
        backoff.OnDown("B", Now + Initial, out _).ShouldBe(OutageScope.Tenant, "known, not suspected: its own");
    }

    [Fact]
    public void NTenantsThatFailAndRecover_LeaveTheStateEmptyAndQuiet()
    {
        var loud = new StrongBox<int>();
        var backoff = New(loud);
        var tenants = Enumerable.Range(0, 50).Select(i => $"tenant-{i}").ToArray();

        foreach (var tenant in tenants)
        {
            backoff.OnDown(tenant, Now, out _);
        }

        backoff.IsEmpty.ShouldBeFalse("control: the entries are there");
        loud.Value.ShouldBe(1);

        foreach (var tenant in tenants)
        {
            backoff.Succeeded(tenant);
        }

        backoff.IsEmpty.ShouldBeTrue();
        backoff.IsQuiet.ShouldBeTrue();
        loud.Value.ShouldBe(0, "the group reads quiet again with one read");
        foreach (var tenant in tenants)
        {
            backoff.TryGetDelay(tenant, out _).ShouldBeFalse();
        }
    }

    [Fact]
    public void Prune_RemovesAnEntryExpiredForAFullCeiling_AndKeepsOneExpiredForLess()
    {
        var backoff = New();
        backoff.OnDown("old", Now, out var oldUntil);
        backoff.OnDown("recent", Now + Ceiling, out var recentUntil);

        backoff.Prune(oldUntil + Ceiling);

        backoff.TryGetDelay("old", out _).ShouldBeFalse("expired for a whole ceiling: the curve starts over anyway");
        backoff.TryGetDelay("recent", out _).ShouldBeTrue("expired for less: its next failure doubles");
        (oldUntil + Ceiling).ShouldBeLessThan(recentUntil + Ceiling, "control: the two expiries differ");

        backoff.Prune(recentUntil + Ceiling);
        backoff.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TryGetDelay_RanksTheTenantsThatFailedLeast()
    {
        var backoff = New();
        backoff.OnDown("twice", Now, out _);
        backoff.Succeeded(null);
        backoff.OnDown("twice", Now + Initial, out _);
        backoff.OnDown("once", Now + Initial, out _);

        backoff.TryGetDelay("once", out var once).ShouldBeTrue();
        backoff.TryGetDelay("twice", out var twice).ShouldBeTrue();
        once.ShouldBeLessThan(twice);
    }

    [Fact]
    public async Task ConcurrentFailuresAndSuccesses_LeaveAConsistentCount()
    {
        var loud = new StrongBox<int>();
        var backoff = New(loud);
        var tenants = Enumerable.Range(0, 64).Select(i => $"tenant-{i}").ToArray();

        await Task.WhenAll(tenants.Select(tenant => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                backoff.OnDown(tenant, Now, out _);
                backoff.IsBlocked(tenant, Now, out _);
                backoff.Succeeded(tenant);
            }
        })));

        backoff.IsEmpty.ShouldBeTrue();
        backoff.IsQuiet.ShouldBeTrue();
        loud.Value.ShouldBe(0);
    }
}
