namespace MicroKit.Messaging.UnitTests.Processing;

using System.Runtime.CompilerServices;
using static MicroKit.Messaging.Processing.InboxBackoff;

/// <summary>
/// The inbox back-off of one store (#210): the unit is the (consumer, tenant) pair; the store probe decides
/// the store-wide back-off; the consumer probe a consumer-wide one that never pauses anything; a store-wide
/// verdict wins (A1).
/// </summary>
public sealed class InboxBackoffTests
{
    private const string X = "Tests.ConsumerX, Tests";
    private const string Y = "Tests.ConsumerY, Tests";
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Initial = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HalfOpen = Ceiling + TimeSpan.FromMinutes(1);

    private static InboxBackoff New(StrongBox<int>? loud = null) => new(Initial, Ceiling, loud);

    /// <summary>V1: X's dependency down for A, Y healthy — Y's success for A neither clears nor shortens X's.</summary>
    [Fact]
    public void ASuccessOfAnotherConsumerForTheTenant_LeavesThePairAndItsCurve()
    {
        var backoff = New();
        backoff.OnDown(X, "A", Now, out var until).ShouldBe(OutageScope.Tenant);

        backoff.Succeeded(Y, "A");

        backoff.IsBlocked(X, "A", Now, out var blocked).ShouldBeTrue("Y's success is not X's");
        blocked.ShouldBe(until);
        backoff.IsBlocked(Y, "A", Now, out _).ShouldBeFalse("Y is never held by X's pair");
        backoff.OnDown(X, "A", until, out var next).ShouldBe(OutageScope.Tenant);
        (next - until).ShouldBe(Initial * 2, "X's curve grew: Y's success did not shorten it");

        backoff.Succeeded(X, "A");
        backoff.IsBlocked(X, "A", until, out _).ShouldBeFalse("X's own success clears its pair");
    }

    /// <summary>V3: a tenant's database down — each consumer fails once for it, both pairs wait, nothing is shared.</summary>
    [Fact]
    public void ATenantsDatabaseDown_EachConsumerFailsOnceForIt_AndNothingIsShared()
    {
        var backoff = New();

        backoff.OnDown(X, "A", Now, out _).ShouldBe(OutageScope.Tenant, "the first tenant down: its own, until proven shared");
        backoff.OnDown(Y, "A", Now, out _).ShouldBe(OutageScope.Tenant, "A already failed, for X: A's own cause");

        backoff.IsBlocked(X, "A", Now, out _).ShouldBeTrue();
        backoff.IsBlocked(Y, "A", Now, out _).ShouldBeTrue();
        backoff.IsBlocked(X, "B", Now, out _).ShouldBeFalse("B goes on");
        backoff.IsBlocked(Y, "B", Now, out _).ShouldBeFalse("B goes on");
        var excluded = backoff.ExclusionsAt(Now);
        excluded.ConsumerTypes.ShouldBeEmpty();
        excluded.Pairs.Order().ShouldBe([(X, "A"), (Y, "A")]);
    }

    /// <summary>V4: X fails for A, Y succeeds, X fails for B — X waits for every tenant; Y does not.</summary>
    [Fact]
    public void AConsumerFailingForASecondTenant_AfterAnotherConsumersSuccess_EntersConsumerWideBackOff()
    {
        var backoff = New();
        backoff.OnDown(X, "A", Now, out _).ShouldBe(OutageScope.Tenant);
        backoff.Succeeded(Y, "B");

        backoff.OnDown(X, "B", Now, out var until).ShouldBe(OutageScope.Consumer, "Y proved the store up: X's own dependency");

        until.ShouldBe(Now + Initial, "the inbox curve's first step");
        backoff.IsBlocked(X, "C", Now, out var held).ShouldBeTrue("X waits for a tenant that never failed");
        held.ShouldBe(until);
        backoff.IsBlocked(Y, "C", Now, out _).ShouldBeFalse("the other consumers go on");
        var excluded = backoff.ExclusionsAt(Now);
        excluded.ConsumerTypes.ShouldBe([X]);
        excluded.Pairs.ShouldBeEmpty("X's pairs are covered by X's exclusion");
        backoff.CountUnrecoveredConsumersAt(Now, HalfOpen).ShouldBe(1);
    }

    /// <summary>V7 and A1: with no success in between, a second tenant down is the store's, and holds no consumer.</summary>
    [Fact]
    public void WithNoSuccessInBetween_ASecondTenantDown_IsShared_AndSetsNoConsumerBackOff()
    {
        var backoff = New();
        backoff.OnDown(X, "A", Now, out _).ShouldBe(OutageScope.Tenant);

        backoff.OnDown(X, "B", Now, out _).ShouldBe(OutageScope.Shared, "a second tenant before any success: the store's");

        backoff.IsBlocked(X, "C", Now, out _).ShouldBeFalse("a store-wide verdict sets no consumer back-off");
        backoff.CountUnrecoveredConsumersAt(Now, HalfOpen).ShouldBe(0);

        // The store-wide back-off cleared X's probe with the store's: after a success, X's next failure for a
        // tenant new to it is a first one again.
        backoff.Succeeded(Y, null);
        backoff.OnDown(X, "C", Now, out _).ShouldBe(OutageScope.Tenant);
    }

    /// <summary>A1: MarkSuspected — the store unreachable, a verdict with no tenant — clears every consumer's probe.</summary>
    [Fact]
    public void MarkSuspected_ClearsEveryConsumersPendingProbe()
    {
        var control = New();
        control.OnDown(X, "A", Now, out _);
        control.Succeeded(Y, null);
        control.OnDown(X, "B", Now, out _).ShouldBe(OutageScope.Consumer, "control: X's probe was pending from A");

        var backoff = New();
        backoff.OnDown(X, "A", Now, out _);
        backoff.MarkSuspected();
        backoff.Succeeded(Y, null);

        backoff.OnDown(X, "B", Now, out _).ShouldBe(OutageScope.Tenant, "the store-wide back-off cleared X's probe");
    }

    /// <summary>
    /// V5: X fails only for A and B, Y succeeding in between. Once the back-offs end, X fails again for A and B —
    /// known to X, so its probe is not touched — and X is not held for C, whose success clears X.
    /// </summary>
    [Fact]
    public void TwoTenantsThatNeverRecover_CannotHoldTheConsumerForAThird()
    {
        var backoff = New();
        backoff.OnDown(X, "A", Now, out _).ShouldBe(OutageScope.Tenant);
        backoff.Succeeded(Y, "Z");
        backoff.OnDown(X, "B", Now, out var xUntil).ShouldBe(OutageScope.Consumer, "X held once");

        var later = xUntil;
        backoff.IsBlocked(X, "C", later, out _).ShouldBeFalse("control: X's back-off and its pairs' have ended");
        backoff.OnDown(X, "A", later, out _).ShouldBe(OutageScope.Tenant, "A is known to X: its own");
        backoff.OnDown(X, "B", later, out _).ShouldBe(OutageScope.Tenant, "B is known to X: its own");

        backoff.IsBlocked(X, "C", later, out _).ShouldBeFalse("two tenants that never recover do not hold X for C");
        backoff.Succeeded(X, "C");
        backoff.CountUnrecoveredConsumersAt(later, HalfOpen).ShouldBe(0, "C's success cleared X's back-off");
        backoff.IsBlocked(X, "A", later, out _).ShouldBeTrue("A stays held");
        backoff.IsBlocked(X, "B", later, out _).ShouldBeTrue("B stays held");
    }

    /// <summary>V6: once X's back-off ends, two failures from tenants new to X put it back with a doubled delay.</summary>
    [Fact]
    public void AReenteredConsumerBackOff_DoublesFromThePreviousOne_AndOneSuccessOfTheConsumerClearsIt()
    {
        var backoff = New();
        backoff.OnDown(X, "A", Now, out _);
        backoff.Succeeded(Y, "Z");
        backoff.OnDown(X, "B", Now, out var first).ShouldBe(OutageScope.Consumer);
        (first - Now).ShouldBe(Initial);

        // Y keeps succeeding: without it, the store probe B left pending makes X's next new tenant Shared.
        var ended = first;
        backoff.Succeeded(Y, "Z");
        backoff.OnDown(X, "C", ended, out _).ShouldBe(OutageScope.Tenant, "the first tenant new to X: its probe");
        backoff.Succeeded(Y, "Z");
        backoff.OnDown(X, "D", ended, out var second).ShouldBe(OutageScope.Consumer, "the second: X again");
        (second - ended).ShouldBe(Initial * 2, "the consumer's curve grew from its previous back-off");

        backoff.Succeeded(X, "D");
        backoff.IsBlocked(X, "E", ended, out _).ShouldBeFalse("one success of X clears its back-off");
        backoff.CountUnrecoveredConsumersAt(ended, HalfOpen).ShouldBe(0);
        backoff.OnDown(X, "E", ended, out _).ShouldBe(OutageScope.Tenant, "and its probe: a first failure again");
    }

    /// <summary>
    /// A verdict while the pair or its consumer is in back-off — an attempt in flight when another put it there —
    /// is Held: the later of the two back-offs, no curve grows, no probe moves, no pair is created.
    /// </summary>
    [Fact]
    public void AVerdictWhileThePairOrItsConsumerIsInBackOff_IsHeld_AndChangesNothing()
    {
        var backoff = New();
        backoff.OnDown(X, "A", Now, out var aFirst);
        backoff.Succeeded(Y, "Z");
        backoff.OnDown(X, "A", aFirst, out var aUntil).ShouldBe(OutageScope.Tenant);
        backoff.OnDown(X, "B", aFirst, out var xUntil).ShouldBe(OutageScope.Consumer);
        aUntil.ShouldBeGreaterThan(xUntil, "control: A's pair outlasts X's back-off");

        var inFlight = aFirst + TimeSpan.FromMilliseconds(1);
        backoff.OnDown(X, "A", inFlight, out var heldA).ShouldBe(OutageScope.Held);
        heldA.ShouldBe(aUntil, "the later of the two in force");
        backoff.OnDown(X, "C", inFlight, out var heldC).ShouldBe(OutageScope.Held, "X's back-off holds a tenant new to it");
        heldC.ShouldBe(xUntil);
        backoff.TryGetDelay(X, "C", out _).ShouldBeFalse("a held verdict creates no pair");
        backoff.TryGetDelay(X, "A", out var delay).ShouldBeTrue();
        delay.ShouldBe(Initial * 2, "the attempts in flight did not grow A's curve");
    }

    [Fact]
    public void ExclusionsAt_IsTheSharedNone_WhenNothingIsInForce()
    {
        var backoff = New();
        backoff.ExclusionsAt(Now).ShouldBeSameAs(InboxClaimExclusions.None, "nothing in back-off: no allocation");

        backoff.OnDown(X, "A", Now, out var until);
        backoff.ExclusionsAt(Now).Pairs.ShouldBe([(X, "A")]);
        backoff.ExclusionsAt(until).ShouldBeSameAs(InboxClaimExclusions.None, "ended, its entry kept for the curve");
    }

    [Fact]
    public void Prune_RemovesThePairsAndConsumerBackOffsExpiredForAFullCeiling_AndTheStateGoesQuiet()
    {
        var loud = new StrongBox<int>();
        var backoff = New(loud);
        backoff.OnDown(X, "A", Now, out _);
        backoff.Succeeded(Y, "Z");
        backoff.OnDown(X, "B", Now, out var until).ShouldBe(OutageScope.Consumer);
        backoff.OnDown(Y, "C", until + Ceiling, out var recent);
        loud.Value.ShouldBe(1);

        backoff.Prune(until + Ceiling);

        backoff.TryGetDelay(X, "A", out _).ShouldBeFalse("expired for a whole ceiling: the curve starts over anyway");
        backoff.CountUnrecoveredConsumersAt(until + Ceiling, HalfOpen).ShouldBe(0, "X's back-off pruned too, though within the half-open bound");
        backoff.TryGetDelay(Y, "C", out _).ShouldBeTrue("expired for less: its next failure doubles");

        backoff.Prune(recent + Ceiling);
        backoff.IsEmpty.ShouldBeTrue();
        backoff.Succeeded(Y, null);
        backoff.IsQuiet.ShouldBeTrue();
        loud.Value.ShouldBe(0, "the group reads quiet again with one read");
    }

    /// <summary>V11 (state): a tenant counts once while any of its pairs is unrecovered.</summary>
    [Fact]
    public void CountUnrecoveredTenantsAt_CountsATenantWithTwoPairsOnce_UntilBothRecover()
    {
        var backoff = New();
        backoff.OnDown(X, "A", Now, out _);
        backoff.OnDown(Y, "A", Now, out _);

        backoff.CountUnrecoveredTenantsAt(Now, HalfOpen).ShouldBe(1, "one tenant, two pairs");
        backoff.Succeeded(Y, "A");
        backoff.CountUnrecoveredTenantsAt(Now, HalfOpen).ShouldBe(1, "X's pair still counts A");
        backoff.Succeeded(X, "A");
        backoff.CountUnrecoveredTenantsAt(Now, HalfOpen).ShouldBe(0);
    }

    [Fact]
    public void NPairsThatFailAndRecover_LeaveTheStateEmptyAndQuiet()
    {
        var loud = new StrongBox<int>();
        var backoff = New(loud);
        var tenants = Enumerable.Range(0, 50).Select(i => $"tenant-{i}").ToArray();
        foreach (var tenant in tenants)
        {
            backoff.OnDown(X, tenant, Now, out _);
            backoff.OnDown(Y, tenant, Now, out _);
        }

        backoff.IsEmpty.ShouldBeFalse("control: the entries are there");
        loud.Value.ShouldBe(1);

        foreach (var tenant in tenants)
        {
            backoff.Succeeded(X, tenant);
            backoff.Succeeded(Y, tenant);
        }

        backoff.IsEmpty.ShouldBeTrue();
        backoff.IsQuiet.ShouldBeTrue();
        loud.Value.ShouldBe(0);
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
                var consumer = (i & 1) == 0 ? X : Y;
                backoff.OnDown(consumer, tenant, Now, out _);
                backoff.IsBlocked(consumer, tenant, Now, out _);
                backoff.ExclusionsAt(Now);
                backoff.Succeeded(consumer, tenant);
            }
        })));

        // Each failure is followed, on its thread, by a success of the same consumer: whatever the interleaving,
        // the last one clears what any failure set.
        backoff.IsEmpty.ShouldBeTrue();
        backoff.IsQuiet.ShouldBeTrue();
        loud.Value.ShouldBe(0);
    }
}
