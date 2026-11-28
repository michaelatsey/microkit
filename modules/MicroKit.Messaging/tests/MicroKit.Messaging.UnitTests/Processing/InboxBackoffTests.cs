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
    private const string W = "Tests.ConsumerW, Tests";
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
    /// V17 (#210 D2 as amended): a verdict that holds X for every tenant is explained by X, and sets no store
    /// probe: one later failure of another consumer, for a new tenant, before any success, is that tenant's own,
    /// not the store probe failing.
    /// </summary>
    [Fact]
    public void AConsumerVerdict_SetsNoStoreProbe_SoAnotherConsumersFirstFailureDoesNotPauseTheStore()
    {
        var backoff = New();
        backoff.OnDown(X, "A", Now, out _);
        backoff.Succeeded(Y, "Z");
        backoff.OnDown(X, "B", Now, out _).ShouldBe(OutageScope.Consumer);

        backoff.OnDown(Y, "C", Now, out _).ShouldBe(OutageScope.Tenant, "X's failures are X's: no probe pending");
        backoff.OnDown(Y, "D", Now, out _).ShouldBe(OutageScope.Shared, "control: a second tenant down still trips it");
    }

    /// <summary>
    /// D2 as amended: the store probe X's own first failure set is withdrawn when X's second failure — for a
    /// tenant that already failed for another consumer, with no success since — holds X for every tenant.
    /// </summary>
    [Fact]
    public void AConsumerVerdict_WithdrawsTheStoreProbeItsOwnFirstFailureSet_NotAnotherConsumers()
    {
        var backoff = New();
        backoff.OnDown(Y, "B", Now, out _);
        backoff.Succeeded(Y, "Z");
        backoff.OnDown(X, "A", Now, out _).ShouldBe(OutageScope.Tenant, "X's first failure sets the store probe");

        backoff.OnDown(X, "B", Now, out _).ShouldBe(OutageScope.Consumer, "B already failed, for Y: no store probe failing");

        backoff.OnDown(Y, "C", Now, out _).ShouldBe(OutageScope.Tenant, "the probe X set is withdrawn");

        var control = New();
        control.OnDown(X, "A", Now, out _);
        control.Succeeded(Y, "Z");
        control.OnDown(Y, "C", Now, out _).ShouldBe(OutageScope.Tenant, "Y sets the probe");
        control.OnDown(X, "C", Now, out _).ShouldBe(OutageScope.Consumer, "C already failed, for Y");
        control.OnDown(Y, "D", Now, out _).ShouldBe(OutageScope.Shared, "control: another consumer's probe stands");
    }

    /// <summary>
    /// D2 as amended (targeted review): X's consumer-wide verdict does not withdraw the store probe its own
    /// failure set when that tenant failed for another consumer too — X does not explain that failure.
    /// </summary>
    [Fact]
    public void AConsumerVerdict_KeepsTheStoreProbe_WhenItsTenantFailedForAnotherConsumerToo()
    {
        var backoff = New();
        backoff.OnDown(Y, "B", Now, out _);
        backoff.Succeeded(Y, "Z");
        backoff.OnDown(X, "A", Now, out _).ShouldBe(OutageScope.Tenant, "X sets the store probe, for A");
        backoff.OnDown(Y, "A", Now, out _).ShouldBe(OutageScope.Tenant, "A failed for Y too");

        backoff.OnDown(X, "B", Now, out _).ShouldBe(OutageScope.Consumer);

        backoff.OnDown(Y, "C", Now, out _).ShouldBe(OutageScope.Shared, "A's failure is not X's alone: the probe stands");
    }

    /// <summary>
    /// V7 with consumers on record (A25): two tenants counted, failed for by two consumers, with no success between,
    /// are the store-wide evidence — whatever the consumers' records. X and W were each held for every tenant:
    /// X fails for T1, W for T2 — Shared. So does a consumer with no record, and an on-record consumer failing a
    /// new tenant after another consumer set the evidence.
    /// </summary>
    [Fact]
    public void TwoCountedTenants_OfTwoConsumers_AreShared_WhateverTheirRecords()
    {
        var later = Now + (Initial * 2);
        var backoff = New();
        HoldOnce(backoff, X, "A", "B");
        HoldOnce(backoff, W, "P", "Q");
        backoff.IsBlocked(X, "T1", later, out _).ShouldBeFalse("control: X's back-off ended, its record stays");
        backoff.IsBlocked(W, "T2", later, out _).ShouldBeFalse("control: W's too");

        backoff.OnDown(X, "T1", later, out _).ShouldBe(OutageScope.Tenant, "one tenant counted");
        backoff.OnDown(W, "T2", later, out _).ShouldBe(OutageScope.Shared, "two counted, two consumers on record: shared");

        var none = New();
        HoldOnce(none, X, "A", "B");
        none.OnDown(X, "T1", later, out _).ShouldBe(OutageScope.Tenant);
        none.OnDown(Y, "T5", later, out _).ShouldBe(OutageScope.Shared, "a consumer with no record, after X");

        var first = New();
        HoldOnce(first, X, "A", "B");
        first.OnDown(Y, "T5", later, out _).ShouldBe(OutageScope.Tenant, "Y counts T5");
        first.OnDown(X, "T1", later, out _).ShouldBe(OutageScope.Shared, "an on-record consumer after Y");
    }

    /// <summary>
    /// The fan-out (targeted review of 62ca48a, A25): X and W, both on record, handle the same messages. X fails
    /// for T1, W for T1 — known, not counted again, but W joins the failed consumers — then X for T2: two tenants
    /// counted, X not the only consumer that failed: Shared.
    /// </summary>
    [Fact]
    public void TwoConsumersOnRecord_FailingForTheSameTenants_AreShared()
    {
        var later = Now + (Initial * 2);
        var backoff = New();
        HoldOnce(backoff, X, "A", "B");
        HoldOnce(backoff, W, "P", "Q");

        backoff.OnDown(X, "T1", later, out _).ShouldBe(OutageScope.Tenant, "T1 counted");
        backoff.OnDown(W, "T1", later, out _).ShouldBe(OutageScope.Tenant, "T1 known: not counted again; W failed too");
        backoff.OnDown(X, "T2", later, out _).ShouldBe(OutageScope.Shared, "two counted, and X is not the only consumer that failed");
    }

    /// <summary>
    /// V16 with a held backlog filling the batch (A25): only X failed since the last success, and X is on record —
    /// held for every tenant, not cleared since. Two tenants counted are X's own: X is held again on its curve,
    /// and leaves the evidence. The limit: X, X then W, W — each consumer's two failures in a row — hold each
    /// consumer, and nothing is shared. With no record, the same two failures are Shared, as on main.
    /// </summary>
    [Fact]
    public void AConsumerOnRecord_AloneInTheEvidence_IsHeldAgain_AndWithdrawn()
    {
        var later = Now + (Initial * 2);
        var backoff = New();
        HoldOnce(backoff, X, "A", "B");
        HoldOnce(backoff, W, "P", "Q");

        backoff.OnDown(X, "T1", later, out _).ShouldBe(OutageScope.Tenant);
        backoff.OnDown(X, "T2", later, out var xUntil).ShouldBe(OutageScope.Consumer, "only X failed, and X is on record");
        (xUntil - later).ShouldBe(Initial * 2, "held again on X's curve");
        backoff.OnDown(W, "T3", later, out _).ShouldBe(OutageScope.Tenant, "X withdrawn: T3 is the only tenant counted");
        backoff.OnDown(W, "T4", later, out _).ShouldBe(OutageScope.Consumer, "the limit: two in a row of one consumer are its own");

        var cleared = New();
        HoldOnce(cleared, X, "A", "B");
        cleared.Succeeded(X, "H");
        cleared.OnDown(X, "C", later, out _).ShouldBe(OutageScope.Tenant, "after X's success, X has no record");
        cleared.OnDown(X, "D", later, out _).ShouldBe(OutageScope.Shared, "control: two counted, no record: shared, as on main");
    }

    /// <summary>
    /// Finding 2 of the targeted review of 62ca48a (A25): while the store is suspected every tenant counts, and the
    /// exemption holds too: X on record, alone in the evidence, is held again instead of failing the store probe,
    /// so its backlog filling a batch does not abandon it.
    /// </summary>
    [Fact]
    public void WhileTheStoreIsSuspected_AConsumerOnRecord_AloneInTheEvidence_IsHeldAgain()
    {
        var later = Now + (Initial * 2);
        var backoff = New();
        HoldOnce(backoff, X, "A", "B");
        backoff.MarkSuspected();

        backoff.OnDown(X, "T1", later, out _).ShouldBe(OutageScope.Tenant, "the first tenant counted since the suspicion");
        backoff.OnDown(X, "T2", later, out _).ShouldBe(OutageScope.Consumer, "only X, on record: held again");
        backoff.OnDown(Y, "T3", later, out _).ShouldBe(OutageScope.Tenant, "X withdrawn: one tenant counted");
        backoff.OnDown(Y, "T4", later, out _).ShouldBe(OutageScope.Shared, "control: Y has no record");
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

    /// <summary>Holds <paramref name="consumer"/> for every tenant once, at <see cref="Now"/>, around Y's success.</summary>
    private static void HoldOnce(InboxBackoff backoff, string consumer, string first, string second)
    {
        backoff.OnDown(consumer, first, Now, out _);
        backoff.Succeeded(Y, "Z");
        backoff.OnDown(consumer, second, Now, out _).ShouldBe(OutageScope.Consumer, $"control: {consumer} held for every tenant");
    }
}
