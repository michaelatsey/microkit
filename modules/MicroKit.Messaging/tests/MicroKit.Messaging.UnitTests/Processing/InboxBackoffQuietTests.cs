namespace MicroKit.Messaging.UnitTests.Processing;

using System.Reflection;

/// <summary>
/// V13 and A3 (#210): a quiet inbox back-off answers every check of the delivery and retry paths with one
/// volatile read — no lock, no allocation — and its counts allocate nothing, quiet or not.
/// </summary>
public sealed class InboxBackoffQuietTests
{
    private const string X = "Tests.ConsumerX, Tests";
    private const string Y = "Tests.ConsumerY, Tests";
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Initial = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HalfOpen = Ceiling + TimeSpan.FromMinutes(1);

    /// <summary>A deadline for a check that took the lock and blocks on it, never a wait for a green run.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    public static TheoryData<bool> Histories() => [false, true];

    /// <param name="wasLoud">Whether the state was in back-off before, and recovered.</param>
    [Theory]
    [MemberData(nameof(Histories))]
    public async Task AQuietState_AnswersEveryCheck_WithNoLockAndNoAllocation(bool wasLoud)
    {
        var backoff = new InboxBackoff(Initial, Ceiling);
        if (wasLoud)
        {
            backoff.OnDown(X, "A", Now, out _);
            backoff.Succeeded(Y, "B");
            backoff.OnDown(X, "B", Now, out _).ShouldBe(InboxBackoff.OutageScope.Consumer, "control: it was loud");
            backoff.Succeeded(X, "A");
            backoff.Succeeded(X, "B");
        }

        backoff.IsQuiet.ShouldBeTrue("control: quiet");
        Checks(backoff).ShouldBeSameAs(InboxClaimExclusions.None);

        // Another thread holds the state's lock: a check that took it would block until the deadline.
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using (LockOf(backoff).EnterScope())
            {
                held.Set();
                release.Wait();
            }
        });
        holder.Start();
        held.Wait();
        try
        {
            var allocated = await Task.Run(() =>
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1_000; i++)
                {
                    Checks(backoff);
                }

                return GC.GetAllocatedBytesForCurrentThread() - before;
            }).WaitAsync(Deadline);

            allocated.ShouldBe(0L, "a quiet state allocates nothing");
        }
        finally
        {
            release.Set();
            holder.Join();
        }
    }

    /// <summary>A3: the counts the observable instruments read allocate nothing, with entries to count.</summary>
    [Fact]
    public void TheCounts_AllocateNothing_WhileInBackOff()
    {
        var backoff = new InboxBackoff(Initial, Ceiling);
        backoff.OnDown(X, "A", Now, out _);
        backoff.OnDown(Y, "A", Now, out _);
        backoff.Succeeded(Y, "C");
        backoff.OnDown(X, "B", Now, out _).ShouldBe(InboxBackoff.OutageScope.Consumer);
        backoff.CountUnrecoveredTenantsAt(Now, HalfOpen).ShouldBe(2, "control: there is something to count");
        backoff.CountUnrecoveredConsumersAt(Now, HalfOpen).ShouldBe(1, "control: there is something to count");

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            backoff.CountUnrecoveredTenantsAt(Now, HalfOpen);
            backoff.CountUnrecoveredConsumersAt(Now, HalfOpen);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0L);
    }

    /// <summary>Every check the delivery and retry paths make, in the order a batch makes them.</summary>
    private static InboxClaimExclusions Checks(InboxBackoff backoff)
    {
        _ = backoff.IsQuiet;
        backoff.Prune(Now);
        var excluded = backoff.ExclusionsAt(Now);
        _ = backoff.IsEmpty;
        backoff.TryGetDelay(X, "A", out _);
        backoff.IsBlocked(X, "A", Now, out _);
        backoff.Succeeded(X, "A");
        backoff.Succeeded(X, null);
        backoff.CountUnrecoveredTenantsAt(Now, HalfOpen);
        backoff.CountUnrecoveredConsumersAt(Now, HalfOpen);
        return excluded;
    }

    private static Lock LockOf(InboxBackoff backoff)
        => typeof(InboxBackoff).GetField("_lock", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(backoff) as Lock
            ?? throw new InvalidOperationException(
                "InboxBackoff has no private Lock field '_lock': this test holds it to prove a quiet check takes no lock. " +
                "Point it at the state's lock again.");
}
