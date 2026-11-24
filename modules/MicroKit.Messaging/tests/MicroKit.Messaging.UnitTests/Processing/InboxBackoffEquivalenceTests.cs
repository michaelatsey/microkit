namespace MicroKit.Messaging.UnitTests.Processing;

using System.Runtime.CompilerServices;
using Xunit.Abstractions;

/// <summary>
/// V2 (#210): with one consumer, the inbox back-off answers exactly as #124's <see cref="TenantBackoff"/> — the
/// same scope and the same end of back-off for every verdict, and the same answers to every check — on
/// generated sequences of verdicts, successes, suspicions, prunes and clock moves over several tenants.
/// </summary>
/// <remarks>
/// Each sequence is drawn from its seed, which every failure message carries and the test output prints: rerun
/// one with <c>--filter</c> on its case. <see cref="TenantBackoff"/> stays the outbox's, unchanged, and is the
/// oracle here, its group count included.
/// </remarks>
public sealed class InboxBackoffEquivalenceTests(ITestOutputHelper output)
{
    private const string Consumer = "Tests.OnlyConsumer, Tests";
    private const int Steps = 400;
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Initial = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HalfOpen = Ceiling + TimeSpan.FromMinutes(1);
    private static readonly string[] Tenants = ["A", "B", "C", "D", "E"];

    public static TheoryData<int> Seeds()
    {
        var seeds = new TheoryData<int>();
        for (var seed = 1; seed <= 100; seed++)
        {
            seeds.Add(seed);
        }

        return seeds;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void WithOneConsumer_AnswersAsTenantBackoff_AtEveryStep(int seed)
    {
        output.WriteLine($"seed {seed}");
        var random = new Random(seed);
        var oracleGroup = new StrongBox<int>();
        var group = new StrongBox<int>();
        var oracle = new TenantBackoff(Initial, Ceiling, oracleGroup);
        var sut = new InboxBackoff(Initial, Ceiling, group);
        var now = Start;
        var shared = 0;
        var detached = false;

        for (var step = 0; step < Steps; step++)
        {
            var tenant = Tenants[random.Next(Tenants.Length)];
            var roll = random.Next(100);
            string op;
            if (roll < 40)
            {
                op = $"OnDown({tenant})";
                var expected = oracle.OnDown(tenant, now, out var expectedUntil);
                var actual = sut.OnDown(Consumer, tenant, now, out var actualUntil);
                actual.ToString().ShouldBe(expected.ToString(), At(seed, step, op, now));
                actualUntil.ShouldBe(expectedUntil, At(seed, step, op, now));
                shared += expected is TenantBackoff.OutageScope.Shared ? 1 : 0;
            }
            else if (roll < 55)
            {
                op = $"Succeeded({tenant})";
                oracle.Succeeded(tenant);
                sut.Succeeded(Consumer, tenant);
            }
            else if (roll < 60)
            {
                op = "Succeeded(null)";
                oracle.Succeeded(null);
                sut.Succeeded(Consumer, null);
            }
            else if (roll < 65)
            {
                op = "MarkSuspected";
                oracle.MarkSuspected();
                sut.MarkSuspected();
            }
            else if (roll < 72)
            {
                op = "Prune";
                oracle.Prune(now);
                sut.Prune(now);
            }
            else if (roll < 73 && !detached)
            {
                // An instance released (#191): it leaves the group's count, and nothing it does later reaches it.
                op = "Detach";
                oracle.Detach();
                sut.Detach();
                detached = true;
            }
            else
            {
                now = Advance(random, oracle, tenant, now);
                op = $"now = {now:O}";
            }

            Compare(oracle, oracleGroup, sut, group, now, At(seed, step, op, now));
        }

        output.WriteLine($"{Steps} steps, {shared} shared verdicts, ended at {now:O}");
    }

    /// <summary>
    /// A clock move that lands on the boundaries the rules compare with: a tenant's end of back-off, a tick
    /// before it, a whole ceiling past it; or a small or large step.
    /// </summary>
    private static DateTimeOffset Advance(Random random, TenantBackoff oracle, string tenant, DateTimeOffset now)
    {
        var known = oracle.TryGetDelay(tenant, out _);
        oracle.IsBlocked(tenant, now, out var until);
        return random.Next(8) switch
        {
            0 when known && until > now => until,
            1 when known && until > now => until - TimeSpan.FromTicks(1),
            2 when known && until + Ceiling > now => until + Ceiling,
            3 => now + TimeSpan.FromTicks(1),
            4 => now + Initial,
            5 => now + TimeSpan.FromSeconds(random.Next(1, 60)),
            6 => now + TimeSpan.FromSeconds(random.Next(0, (int)(Ceiling.TotalSeconds * 2))),
            _ => now,
        };
    }

    private static void Compare(
        TenantBackoff oracle, StrongBox<int> oracleGroup, InboxBackoff sut, StrongBox<int> group, DateTimeOffset now, string at)
    {
        foreach (var tenant in Tenants)
        {
            var expectedBlocked = oracle.IsBlocked(tenant, now, out var expectedUntil);
            sut.IsBlocked(Consumer, tenant, now, out var actualUntil).ShouldBe(expectedBlocked, $"{at}: IsBlocked({tenant})");
            if (expectedBlocked)
            {
                actualUntil.ShouldBe(expectedUntil, $"{at}: IsBlocked({tenant}) until");
            }

            var expectedKnown = oracle.TryGetDelay(tenant, out var expectedDelay);
            sut.TryGetDelay(Consumer, tenant, out var actualDelay).ShouldBe(expectedKnown, $"{at}: TryGetDelay({tenant})");
            actualDelay.ShouldBe(expectedDelay, $"{at}: TryGetDelay({tenant}) delay");
        }

        var blocked = oracle.BlockedAt(now);
        var excluded = sut.ExclusionsAt(now);
        excluded.ConsumerTypes.ShouldBeEmpty($"{at}: one consumer is never held consumer-wide");
        excluded.Pairs.Select(p => p.ConsumerType).ShouldAllBe(c => c == Consumer, at);
        excluded.Pairs.Select(p => p.TenantId).Order(StringComparer.Ordinal)
            .ShouldBe(blocked.Order(StringComparer.Ordinal), $"{at}: BlockedAt");
        if (blocked.Length == 0)
        {
            excluded.ShouldBeSameAs(InboxClaimExclusions.None, $"{at}: nothing in force");
        }

        sut.IsEmpty.ShouldBe(oracle.IsEmpty, $"{at}: IsEmpty");
        sut.IsQuiet.ShouldBe(oracle.IsQuiet, $"{at}: IsQuiet");
        group.Value.ShouldBe(oracleGroup.Value, $"{at}: the group's count");
        sut.CountUnrecoveredTenantsAt(now, HalfOpen).ShouldBe(oracle.CountUnrecoveredAt(now, HalfOpen), $"{at}: unrecovered");
        sut.CountUnrecoveredConsumersAt(now, HalfOpen).ShouldBe(0, $"{at}: no consumer back-off");
    }

    private static string At(int seed, int step, string op, DateTimeOffset now) => $"seed {seed}, step {step}, {op} at {now:O}";
}
