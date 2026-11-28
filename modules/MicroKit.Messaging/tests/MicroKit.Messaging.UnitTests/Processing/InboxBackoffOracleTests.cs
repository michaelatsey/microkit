namespace MicroKit.Messaging.UnitTests.Processing;

using Xunit.Abstractions;
using static MicroKit.Messaging.Processing.InboxBackoff;

/// <summary>
/// The store probe of #210 (A25) against a reference model written from the rule's text: plain sets and maps,
/// no code shared with <see cref="InboxBackoff"/> — the curve included. On seeded sequences over three consumers
/// and four tenants (down verdicts, successes, suspicions, prunes, clock moves, consumer back-offs coming on record
/// and ending), every down verdict gets the same scope and the same end of back-off from both.
/// </summary>
/// <remarks>
/// <para>
/// Each sequence is drawn from its seed, which every failure message carries and the test output prints.
/// <see cref="TheSequences_ReachEveryRule"/> checks that the seeds exercise each rule of the model, so a rule the
/// generator never reaches cannot pass unseen.
/// </para>
/// <para>
/// It checks the code against the rule's text, and cannot find a flaw in the rule itself: its structure follows
/// that text, as the implementation's does, so a sequence the rule answers wrongly is answered the same way by
/// both. What the rule should answer is pinned by the hand-written expectations of
/// <see cref="InboxBackoffTests"/> and, on PostgreSQL, <c>ConsumerBackoffInboxTests</c>.
/// </para>
/// </remarks>
public sealed class InboxBackoffOracleTests(ITestOutputHelper output)
{
    private const int Steps = 400;
    private const int SeedCount = 200;
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Initial = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(5);
    private static readonly string[] Consumers = ["Tests.X, Tests", "Tests.Y, Tests", "Tests.W, Tests"];
    private static readonly string[] Tenants = ["A", "B", "C", "D"];

    public static TheoryData<int> Seeds()
    {
        var seeds = new TheoryData<int>();
        for (var seed = 1; seed <= SeedCount; seed++)
        {
            seeds.Add(seed);
        }

        return seeds;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void InboxBackoff_AnswersAsTheModel_AtEveryStep(int seed)
    {
        output.WriteLine($"seed {seed}");
        var model = new Model();
        var sut = new InboxBackoff(Initial, Ceiling);
        Run(seed, model, (step, op, now, consumer, tenant) =>
        {
            var expected = model.OnDown(consumer, tenant, now, out var expectedUntil);
            var actual = sut.OnDown(consumer, tenant, now, out var actualUntil);
            var at = $"seed {seed}, step {step}, {op} at {now:O}";
            actual.ToString().ShouldBe(expected.ToString(), at);
            actualUntil.ShouldBe(expectedUntil, at);
        }, sut);

        output.WriteLine($"{Steps} steps: {model.Reached}");
    }

    /// <summary>The seeds reach every rule of the model, each more than once.</summary>
    [Fact]
    public void TheSequences_ReachEveryRule()
    {
        var total = new Reach();
        for (var seed = 1; seed <= SeedCount; seed++)
        {
            var model = new Model();
            Run(seed, model, (_, _, now, consumer, tenant) => model.OnDown(consumer, tenant, now, out _), sut: null);
            total.Add(model.Reached);
        }

        output.WriteLine(total.ToString());
        total.Held.ShouldBeGreaterThan(10);
        total.CountedWhileSuspected.ShouldBeGreaterThan(10, "a known tenant counted while suspected");
        total.KnownNotCounted.ShouldBeGreaterThan(10);
        total.SharedOneConsumer.ShouldBeGreaterThan(10, "two tenants of one consumer with no record");
        total.SharedSeveralConsumers.ShouldBeGreaterThan(10);
        total.Exempt.ShouldBeGreaterThan(10, "a consumer on record alone in the evidence");
        total.ConsumerProbe.ShouldBeGreaterThan(10);
        total.WithdrawnTenants.ShouldBeGreaterThan(10);
        total.KeptTenants.ShouldBeGreaterThan(10, "a tenant another consumer failed for stays counted");
        total.ResetBySuccess.ShouldBeGreaterThan(10);
        total.ResetByNoEntry.ShouldBeGreaterThan(10);
        total.RecordPruned.ShouldBeGreaterThan(10);
    }

    private static void Run(
        int seed, Model model, Action<int, string, DateTimeOffset, string, string> down, InboxBackoff? sut)
    {
        var random = new Random(seed);
        var now = Start;
        for (var step = 0; step < Steps; step++)
        {
            var consumer = Consumers[random.Next(Consumers.Length)];
            var tenant = Tenants[random.Next(Tenants.Length)];
            var roll = random.Next(100);
            if (roll < 50)
            {
                down(step, $"OnDown({consumer}, {tenant})", now, consumer, tenant);
            }
            else if (roll < 60)
            {
                var succeeded = random.Next(4) == 0 ? null : tenant;
                model.Succeeded(consumer, succeeded);
                sut?.Succeeded(consumer, succeeded);
            }
            else if (roll < 63)
            {
                model.MarkSuspected();
                sut?.MarkSuspected();
            }
            else if (roll < 68)
            {
                model.Prune(now);
                sut?.Prune(now);
            }
            else
            {
                now = random.Next(7) switch
                {
                    0 => now + TimeSpan.FromTicks(1),
                    1 => now + Initial,
                    2 => now + (Initial * 2),
                    3 => now + TimeSpan.FromSeconds(random.Next(1, 60)),
                    4 => now + Ceiling,
                    5 => now + TimeSpan.FromSeconds(random.Next(0, (int)(Ceiling.TotalSeconds * 3))),
                    _ => now,
                };
            }
        }
    }

    /// <summary>The model's counters: which rule each verdict took.</summary>
    private sealed class Reach
    {
        public int Held { get; set; }

        public int CountedWhileSuspected { get; set; }

        public int KnownNotCounted { get; set; }

        public int SharedOneConsumer { get; set; }

        public int SharedSeveralConsumers { get; set; }

        public int Exempt { get; set; }

        public int ConsumerProbe { get; set; }

        public int WithdrawnTenants { get; set; }

        public int KeptTenants { get; set; }

        public int ResetBySuccess { get; set; }

        public int ResetByNoEntry { get; set; }

        public int RecordPruned { get; set; }

        public void Add(Reach other)
        {
            Held += other.Held;
            CountedWhileSuspected += other.CountedWhileSuspected;
            KnownNotCounted += other.KnownNotCounted;
            SharedOneConsumer += other.SharedOneConsumer;
            SharedSeveralConsumers += other.SharedSeveralConsumers;
            Exempt += other.Exempt;
            ConsumerProbe += other.ConsumerProbe;
            WithdrawnTenants += other.WithdrawnTenants;
            KeptTenants += other.KeptTenants;
            ResetBySuccess += other.ResetBySuccess;
            ResetByNoEntry += other.ResetByNoEntry;
            RecordPruned += other.RecordPruned;
        }

        public override string ToString()
            => $"held {Held}, counted while suspected {CountedWhileSuspected}, known not counted {KnownNotCounted}, "
                + $"shared one consumer {SharedOneConsumer}, shared several {SharedSeveralConsumers}, exempt {Exempt}, "
                + $"consumer probe {ConsumerProbe}, withdrawn tenants {WithdrawnTenants}, kept tenants {KeptTenants}, "
                + $"reset by success {ResetBySuccess}, reset by no entry {ResetByNoEntry}, "
                + $"record pruned {RecordPruned}";
    }

    /// <summary>
    /// The rule's text, as a model. Pairs and consumer back-offs on the inbox curve (doubling from the last failure
    /// unless a whole ceiling went by past the end, capped at the ceiling); a verdict while the pair or its consumer
    /// is in back-off is Held and changes nothing. The evidence — the counted tenants, one per verdict that counted,
    /// and the failed consumers with their tenants — is reset on any success, on Shared, on MarkSuspected and when
    /// no entry is left. A tenant counts when it had no entry for any consumer before the verdict, or while the store
    /// is suspected. Two counted: Shared, unless the only failed consumer is this one and it has a consumer-wide
    /// back-off on record — then Consumer, held again on its curve. Otherwise the consumer probe: a second tenant new
    /// to the consumer is Consumer. A Consumer verdict withdraws the consumer and the tenants only it failed for.
    /// </summary>
    private sealed class Model
    {
        private readonly Dictionary<(string Consumer, string Tenant), (DateTimeOffset Until, TimeSpan Delay)> _pairs = [];
        private readonly Dictionary<string, (DateTimeOffset Until, TimeSpan Delay)> _backoffs = [];
        private readonly Dictionary<string, string> _probes = [];
        private readonly List<string> _counted = [];
        private readonly Dictionary<string, HashSet<string>> _failed = [];
        private bool _suspected;

        public Reach Reached { get; } = new();

        public OutageScope OnDown(string consumer, string tenant, DateTimeOffset now, out DateTimeOffset until)
        {
            var held = false;
            until = default;
            if (_pairs.TryGetValue((consumer, tenant), out var pair) && now < pair.Until)
            {
                until = pair.Until;
                held = true;
            }

            if (_backoffs.TryGetValue(consumer, out var record) && now < record.Until)
            {
                until = held && pair.Until > record.Until ? pair.Until : record.Until;
                held = true;
            }

            if (held)
            {
                Reached.Held++;
                return OutageScope.Held;
            }

            var tenantKnown = _pairs.Keys.Any(k => k.Tenant == tenant);
            var pairKnown = _pairs.TryGetValue((consumer, tenant), out var previous);
            var delay = Next(pairKnown ? previous : null, now);
            _pairs[(consumer, tenant)] = (now + delay, delay);
            until = now + delay;

            if (!tenantKnown || _suspected)
            {
                if (tenantKnown)
                {
                    Reached.CountedWhileSuspected++;
                }

                _counted.Add(tenant);
            }
            else
            {
                Reached.KnownNotCounted++;
            }

            if (!_failed.TryGetValue(consumer, out var failedFor))
            {
                _failed[consumer] = failedFor = [];
            }

            failedFor.Add(tenant);

            OutageScope scope;
            if (_counted.Count >= 2)
            {
                // A record in force returned Held above: every exemption is on a record that ended.
                if (_failed.Count == 1 && _backoffs.ContainsKey(consumer))
                {
                    Reached.Exempt++;
                    scope = Hold(consumer, now, ref until);
                }
                else
                {
                    if (_failed.Count == 1)
                    {
                        Reached.SharedOneConsumer++;
                    }
                    else
                    {
                        Reached.SharedSeveralConsumers++;
                    }

                    _suspected = true;
                    Reset();
                    _probes.Clear();
                    scope = OutageScope.Shared;
                }
            }
            else if (!pairKnown && _probes.TryGetValue(consumer, out var probe) && probe != tenant)
            {
                Reached.ConsumerProbe++;
                scope = Hold(consumer, now, ref until);
            }
            else
            {
                if (!pairKnown)
                {
                    _probes[consumer] = tenant;
                }

                scope = OutageScope.Tenant;
            }

            if (scope is OutageScope.Consumer)
            {
                var mine = _failed[consumer];
                _failed.Remove(consumer);
                foreach (var failed in mine)
                {
                    if (_failed.Values.Any(other => other.Contains(failed)))
                    {
                        Reached.KeptTenants++;
                    }
                    else if (_counted.RemoveAll(t => t == failed) > 0)
                    {
                        Reached.WithdrawnTenants++;
                    }
                }
            }

            Tidy();
            return scope;
        }

        public void Succeeded(string consumer, string? tenant)
        {
            if (tenant is not null)
            {
                _pairs.Remove((consumer, tenant));
            }

            _probes.Remove(consumer);
            _backoffs.Remove(consumer);
            if (_counted.Count > 0 || _failed.Count > 0)
            {
                Reached.ResetBySuccess++;
            }

            Reset();
            _suspected = false;
            Tidy();
        }

        public void MarkSuspected()
        {
            _suspected = true;
            Reset();
            _probes.Clear();
            Tidy();
        }

        public void Prune(DateTimeOffset now)
        {
            foreach (var key in _pairs.Where(p => now >= p.Value.Until + Ceiling).Select(p => p.Key).ToList())
            {
                _pairs.Remove(key);
            }

            foreach (var key in _backoffs.Where(b => now >= b.Value.Until + Ceiling).Select(b => b.Key).ToList())
            {
                _backoffs.Remove(key);
                Reached.RecordPruned++;
            }

            Tidy();
        }

        private OutageScope Hold(string consumer, DateTimeOffset now, ref DateTimeOffset until)
        {
            var delay = Next(_backoffs.TryGetValue(consumer, out var record) ? record : null, now);
            _backoffs[consumer] = (now + delay, delay);
            _probes.Remove(consumer);
            if (now + delay > until)
            {
                until = now + delay;
            }

            return OutageScope.Consumer;
        }

        private void Reset()
        {
            _counted.Clear();
            _failed.Clear();
        }

        /// <summary>No entry left resets the evidence; a consumer with no entry left loses its probe.</summary>
        private void Tidy()
        {
            if (_pairs.Count == 0 && (_counted.Count > 0 || _failed.Count > 0))
            {
                Reached.ResetByNoEntry++;
                Reset();
            }

            foreach (var consumer in _probes.Keys.ToList())
            {
                if (!_pairs.Keys.Any(k => k.Consumer == consumer))
                {
                    _probes.Remove(consumer);
                }
            }
        }

        private static TimeSpan Next((DateTimeOffset Until, TimeSpan Delay)? previous, DateTimeOffset now)
        {
            var delay = previous is not { } last || now >= last.Until + Ceiling ? Initial : last.Delay * 2;
            return delay > Ceiling ? Ceiling : delay;
        }
    }
}
