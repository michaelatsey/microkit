using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The inbox back-off per (consumer, tenant) pair (#210), on PostgreSQL with two consumers of one message:
/// X (<see cref="FirstGuardHandler"/>) and Y (<see cref="SecondGuardHandler"/>). One consumer's success no
/// longer clears another's back-off, one consumer's failure no longer holds another, a consumer whose own
/// dependency is down is held for every tenant without pausing anything, and a store-wide outage still pauses
/// and abandons as before.
/// </summary>
/// <remarks>
/// <see cref="GuardHost"/> on a <see cref="FakeTimeProvider"/>: the inbox's polling interval (50 ms there) is
/// the back-off's base. A delivery runs X, then Y. Each test drives the retry batches itself.
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class ConsumerBackoffInboxTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Polling = TimeSpan.FromMilliseconds(50);
    private const string X = FirstGuardHandler.HandlerName;
    private const string Y = SecondGuardHandler.HandlerName;
    private const string NoTenant = "-";
    private static readonly string KeyOfX = MessageConsumerKey.Of(typeof(FirstGuardHandler));
    private static readonly string KeyOfY = MessageConsumerKey.Of(typeof(SecondGuardHandler));

    /// <summary>
    /// V1, the issue's case: X's dependency down for A, Y healthy. Y's deliveries for A run its handler and are
    /// never deferred; X's rows for A wait on a growing delay, which Y's successes for A do not shorten.
    /// </summary>
    [DockerRequiredFact]
    public async Task V1_XDownForA_YHealthy_YRunsAndIsNeverDeferred_AndXsRowsWaitOnAGrowingDelay()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Down(X, "A");

        (await rig.ReceiveAsync("A")).Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        rig.TakeCalls().ShouldBe([$"{X}:A", $"{Y}:A"]);
        (await rig.ReceiveAsync("A")).Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        rig.TakeCalls().ShouldBe([$"{Y}:A"], "X is held for A; Y's success for A did not clear it");
        (await rig.RowsOfAsync(KeyOfX)).ShouldAllBe(r => r.NextRetryAtUtc == Now + Polling && r.RetryCount == 0);

        // A retry of X for A fails again: X's curve doubles, and the row X held unattempted waits with it.
        rig.Clock.Advance(Polling);
        (await rig.Host.RetryOnceAsync()).AbortReason.ShouldBe(InboxBatchAbortReason.None);
        rig.TakeCalls().ShouldBe([$"{X}:A"], "one attempt; the other row of the pair waits unattempted");
        var second = Now + Polling + (Polling * 2);
        (await rig.RowsOfAsync(KeyOfX)).ShouldAllBe(r => r.NextRetryAtUtc == second);

        // Y keeps succeeding for A in between: X's delay does not start over.
        (await rig.ReceiveAsync("A")).Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        rig.TakeCalls().ShouldBe([$"{Y}:A"]);
        rig.Clock.SetUtcNow(second);
        await rig.Host.RetryOnceAsync();
        rig.TakeCalls().ShouldBe([$"{X}:A"]);
        (await rig.RowsOfAsync(KeyOfX)).ShouldAllBe(r => r.NextRetryAtUtc == second + (Polling * 4) && r.RetryCount == 0);

        (await rig.RowsOfAsync(KeyOfY)).ShouldAllBe(r => r.Status == InboxMessageStatus.Processed, "Y is never deferred");
        rig.Host.Logs.Entries.ShouldNotContain(e => e.EventId == 2015, "X failed for one tenant only: never consumer-wide");
    }

    /// <summary>
    /// V3: a tenant's database down — X and Y fail for A, nothing else fails. Each consumer of A makes its one
    /// attempt (the documented cost), both pairs wait, nothing pauses, no batch is abandoned, and B goes on.
    /// </summary>
    [DockerRequiredFact]
    public async Task V3_ATenantsDatabaseDown_BothPairsWait_NothingPausesOrIsAbandoned_AndBGoesOn()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Down(X, "A");
        rig.Down(Y, "A");

        (await rig.ReceiveAsync("A")).Disposition.ShouldBe(ReceiveDisposition.Acknowledge, "A's own cause: no pause");
        rig.TakeCalls().ShouldBe([$"{X}:A", $"{Y}:A"]);
        (await rig.ReceiveAsync("B")).Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        rig.TakeCalls().ShouldBe([$"{X}:B", $"{Y}:B"], "B is not held behind A");
        (await rig.ReceiveAsync("A")).Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        rig.TakeCalls().ShouldBeEmpty("both pairs of A are held");

        rig.Clock.Advance(Polling);
        (await rig.Host.RetryOnceAsync()).AbortReason.ShouldBe(InboxBatchAbortReason.None, "A's own cause: the batch goes on");
        var rows = await rig.Host.InboxAsync();
        rows.Where(r => r.TenantId == "A").ShouldAllBe(r => r.Status == InboxMessageStatus.Failed && r.RetryCount == 0);
        rows.Where(r => r.TenantId == "B").ShouldAllBe(r => r.Status == InboxMessageStatus.Processed);
        rig.Host.Logs.Entries.ShouldNotContain(e => e.EventId == 2015);
    }

    /// <summary>
    /// V4: X fails for A, Y succeeds, X fails for B — X's own dependency. A delivery of X for C runs no handler
    /// of X and is deferred to X's back-off; Y is never deferred; the transport never pauses; 2015 once.
    /// </summary>
    [DockerRequiredFact]
    public async Task V4_XFailingForTwoNewTenantsAroundYsSuccess_IsHeldForEveryTenant_YIsNot_AndNothingPauses()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Down(X, "A");
        rig.Down(X, "B");

        (await rig.ReceiveAsync("A")).Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        (await rig.ReceiveAsync("B")).Disposition.ShouldBe(ReceiveDisposition.Acknowledge, "X's own: never a pause");
        rig.TakeCalls().ShouldBe([$"{X}:A", $"{Y}:A", $"{X}:B", $"{Y}:B"]);
        rig.Host.Logs.Entries.Count(e => e.EventId == 2015).ShouldBe(1, "X entered consumer-wide back-off");
        rig.Host.Logs.Entries.ShouldContain(
            e => e.EventId == 2013 && e.RenderedMessage.Contains("for tenant B", StringComparison.Ordinal),
            "the verdict that held X also put its pair (X, B) in back-off, reported as the retry loop does");

        var (c, result) = await rig.ReceiveWithIdAsync("C");
        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        rig.TakeCalls().ShouldBe([$"{Y}:C"], "X runs no handler for a tenant that never failed; Y runs");
        var held = (await rig.Host.InboxAsync()).Single(r => r.MessageId.Value == c && r.ConsumerType == KeyOfX);
        held.Status.ShouldBe(InboxMessageStatus.Failed);
        held.RetryCount.ShouldBe(0);
        held.NextRetryAtUtc.ShouldBe(Now + Polling, "X's consumer back-off");
        (await rig.RowsOfAsync(KeyOfY)).ShouldAllBe(r => r.Status == InboxMessageStatus.Processed, "Y is never deferred");
        rig.Host.Logs.Entries.Count(e => e.EventId == 2015).ShouldBe(1, "once per entry, not per held delivery");
        rig.Host.Logs.Entries.ShouldContain(e => e.EventId == 2014 && e.RenderedMessage.Contains("for every tenant"));
    }

    /// <summary>
    /// V5: X fails only for A and B, Y succeeding in between — X held once. Once X's back-off and the pairs' end,
    /// X fails again for A and B, known to X, then a delivery of X for C runs, succeeds and clears X; A and B stay
    /// held.
    /// </summary>
    [DockerRequiredFact]
    public async Task V5_TwoTenantsThatNeverRecover_DoNotHoldXForAThird()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Down(X, "A");
        rig.Down(X, "B");
        await rig.ReceiveAsync("A");
        await rig.ReceiveAsync("B");
        rig.Host.Logs.Entries.Count(e => e.EventId == 2015).ShouldBe(1, "control: X held once");
        rig.TakeCalls();

        rig.Clock.Advance(Polling);
        (await rig.ReceiveAsync("A")).Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        (await rig.ReceiveAsync("B")).Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        rig.TakeCalls().ShouldBe([$"{X}:A", $"{Y}:A", $"{X}:B", $"{Y}:B"], "the back-offs ended: X tries A and B again");

        var (c, _) = await rig.ReceiveWithIdAsync("C");
        rig.TakeCalls().ShouldBe([$"{X}:C", $"{Y}:C"], "A and B, known to X, did not hold it for C");
        (await rig.Host.InboxAsync()).Single(r => r.MessageId.Value == c && r.ConsumerType == KeyOfX)
            .Status.ShouldBe(InboxMessageStatus.Processed);
        rig.Host.Logs.Entries.Count(e => e.EventId == 2015).ShouldBe(1);

        await rig.ReceiveAsync("A");
        rig.TakeCalls().ShouldBe([$"{Y}:A"], "A stays held for X");
    }

    /// <summary>
    /// A retry batch in which X enters consumer-wide back-off: Y's row between X's first two failures succeeds,
    /// the second failure holds X for every tenant, and X's later rows of the batch wait unattempted. 2013 names
    /// the two tenants that failed for X, never the tenants X's back-off alone held, which 2015 reports once.
    /// </summary>
    [DockerRequiredFact]
    public async Task ARetryBatchInWhichXEntersConsumerWideBackOff_Reports2013OnlyForTheTenantsThatFailed()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        foreach (var tenant in new[] { "T1", "T2", "T3", "T4" })
        {
            rig.Down(X, tenant);
        }

        await rig.SeedFailedAsync<FirstGuardHandler>("T1");
        await rig.SeedFailedAsync<SecondGuardHandler>("T9");
        await rig.SeedFailedAsync<FirstGuardHandler>("T2");
        var t3 = await rig.SeedFailedAsync<FirstGuardHandler>("T3");
        var t4 = await rig.SeedFailedAsync<FirstGuardHandler>("T4");

        (await rig.Host.RetryOnceAsync()).AbortReason.ShouldBe(InboxBatchAbortReason.None, "X's own: the batch goes on");

        rig.TakeCalls().ShouldBe([$"{X}:T1", $"{Y}:T9", $"{X}:T2"], "X's later rows wait unattempted");
        var rows = await rig.Host.InboxAsync();
        rows.Where(r => r.RowId == t3.RowId || r.RowId == t4.RowId).ShouldAllBe(r => r.NextRetryAtUtc == Now + Polling && r.RetryCount == 1);
        var deferrals = rig.Host.Logs.Entries.Where(e => e.EventId == 2013).Select(e => e.RenderedMessage).ToList();
        deferrals.Count.ShouldBe(2, string.Join(" | ", deferrals));
        deferrals.ShouldContain(m => m.Contains("for tenant T1", StringComparison.Ordinal));
        deferrals.ShouldContain(m => m.Contains("for tenant T2", StringComparison.Ordinal));
        rig.Host.Logs.Entries.Count(e => e.EventId == 2015).ShouldBe(1);
    }

    /// <summary>
    /// V16 (#210 D4 as amended): retries only, no transport traffic. X's backlog for tenants new to the store
    /// probe heads the claim and Y's healthy rows follow. From X's first failure on, the batch alternates
    /// consumers: Y's success comes before X's second failure, which holds X for every tenant; the batch is not
    /// abandoned, and Y's rows are processed in it.
    /// </summary>
    [DockerRequiredFact]
    public async Task V16_AHeldConsumersBacklogHeadingTheClaim_DoesNotAbandonTheBatch_AndTheOtherConsumersRowsAreProcessed()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        string[] tenants = ["T1", "T2", "T3", "T4", "T5"];
        foreach (var tenant in tenants)
        {
            rig.Down(X, tenant);
            await rig.SeedFailedAsync<FirstGuardHandler>(tenant);
        }

        for (var i = 0; i < 3; i++)
        {
            await rig.SeedFailedAsync<SecondGuardHandler>("T9");
        }

        (await rig.Host.RetryOnceAsync()).AbortReason.ShouldBe(InboxBatchAbortReason.None, "X's own: the batch goes on");

        rig.TakeCalls().ShouldBe(
            [$"{X}:T1", $"{Y}:T9", $"{X}:T2", $"{Y}:T9", $"{Y}:T9"],
            "a row of Y between X's failures; X's later rows wait unattempted");
        (await rig.RowsOfAsync(KeyOfY)).ShouldAllBe(r => r.Status == InboxMessageStatus.Processed, "Y's rows, in the same batch");
        (await rig.RowsOfAsync(KeyOfX)).ShouldAllBe(r => r.Status == InboxMessageStatus.Failed && r.RetryCount == 1);
        rig.Host.Logs.Entries.Count(e => e.EventId == 2015).ShouldBe(1, "X entered its consumer-wide back-off");
    }

    /// <summary>
    /// V17 (#210 D2 as amended): X enters consumer-wide back-off, then Y fails once for a new tenant with no success
    /// in between: X's verdict set no store probe, so nothing pauses and the batch goes on.
    /// </summary>
    [DockerRequiredFact]
    public async Task V17_AfterXIsHeldConsumerWide_OneFailureOfYForANewTenant_PausesNothing()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Down(X, "A");
        rig.Down(X, "B");
        rig.Down(Y, "D");
        await rig.SeedFailedAsync<FirstGuardHandler>("A");
        await rig.SeedFailedAsync<SecondGuardHandler>("T9");
        await rig.SeedFailedAsync<FirstGuardHandler>("B");
        var d = await rig.SeedFailedAsync<SecondGuardHandler>("D");

        (await rig.Host.RetryOnceAsync()).AbortReason.ShouldBe(InboxBatchAbortReason.None, "Y's first failure for D is D's own");

        rig.TakeCalls().ShouldBe([$"{X}:A", $"{Y}:T9", $"{X}:B", $"{Y}:D"]);
        rig.Host.Logs.Entries.Count(e => e.EventId == 2015).ShouldBe(1, "control: X was held consumer-wide");
        var row = (await rig.Host.InboxAsync()).Single(r => r.RowId == d.RowId);
        row.Status.ShouldBe(InboxMessageStatus.Failed);
        row.NextRetryAtUtc.ShouldBe(Now + Polling, "Y's pair for D waits; nothing was abandoned");
    }

    /// <summary>V7: no success anywhere — two tenants down for any consumers — pause and abandon, as before #210.</summary>
    [DockerRequiredFact]
    public async Task V7_NoSuccessAnywhere_ASecondTenantDown_PausesTheTransport_AndARetryBatchIsAbandoned()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.EverythingDown = true;

        (await rig.ReceiveAsync("A")).Disposition.ShouldBe(ReceiveDisposition.Acknowledge, "the first tenant down: its own");
        (await rig.ReceiveAsync("B")).Disposition.ShouldBe(ReceiveDisposition.PauseAndRedeliver, "a second, no success: shared");

        rig.Clock.Advance(Polling);
        (await rig.Host.RetryOnceAsync()).AbortReason.ShouldBe(InboxBatchAbortReason.DependencyUnavailable);
        rig.Host.Logs.Entries.ShouldNotContain(e => e.EventId == 2015, "a store-wide outage holds no consumer");
    }

    /// <summary>
    /// V8: rows and deliveries with no tenant behave as before #210, also while X is held consumer-wide: X's row
    /// with no tenant is claimed and attempted while its row with a tenant is left out, and a delivery with no
    /// tenant runs X.
    /// </summary>
    [DockerRequiredFact]
    public async Task V8_WhileXIsHeldConsumerWide_ItsRowsAndDeliveriesWithNoTenant_AreHandledAsBefore()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Down(X, "A");
        rig.Down(X, "B");
        await rig.ReceiveAsync("A");
        await rig.ReceiveAsync("B");
        rig.Host.Logs.Entries.Count(e => e.EventId == 2015).ShouldBe(1, "control: X is held consumer-wide");
        rig.TakeCalls();

        var withTenant = await rig.SeedFailedAsync<FirstGuardHandler>("C");
        var withNone = await rig.SeedFailedAsync<FirstGuardHandler>(null);
        await rig.Host.RetryOnceAsync();
        rig.TakeCalls().ShouldBe([$"{X}:{NoTenant}"], "X's row with no tenant is claimed and run; its row for C is left out");
        var rows = await rig.Host.InboxAsync();
        rows.Single(r => r.RowId == withNone.RowId).Status.ShouldBe(InboxMessageStatus.Processed);
        var untouched = rows.Single(r => r.RowId == withTenant.RowId);
        untouched.ClaimToken.ShouldBeNull("never claimed");
        untouched.RetryCount.ShouldBe(1);

        // X's success with no tenant cleared it; X is held again by two new tenants around Y's success.
        rig.Down(X, "D");
        rig.Down(X, "E");
        await rig.ReceiveAsync("D");
        await rig.ReceiveAsync("E");
        rig.Host.Logs.Entries.Count(e => e.EventId == 2015).ShouldBe(2, "control: X is held consumer-wide again");
        rig.TakeCalls();

        (await rig.ReceiveAsync(null)).Disposition.ShouldBe(ReceiveDisposition.Acknowledge);
        rig.TakeCalls().ShouldBe([$"{X}:{NoTenant}", $"{Y}:{NoTenant}"], "a delivery with no tenant is never held by X's back-off");
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<Guid, string> _tenantOf = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _down = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _calls = new();
        private int _order;

        private Rig(GuardHost host, FakeTimeProvider clock)
        {
            Host = host;
            Clock = clock;
            foreach (var handler in new[] { X, Y })
            {
                Host.Probe.Scripts[handler] = call =>
                {
                    var tenant = _tenantOf[call.Message.Id];
                    _calls.Enqueue($"{handler}:{tenant}");
                    var down = EverythingDown || (_down.TryGetValue(handler, out var tenants) && tenants.ContainsKey(tenant));
                    return down
                        ? throw new InboxDependencyUnavailableException($"{handler}'s dependency is down for tenant '{tenant}'.")
                        : Task.CompletedTask;
                };
            }
        }

        public GuardHost Host { get; }

        public FakeTimeProvider Clock { get; }

        public bool EverythingDown { get; set; }

        public static async Task<Rig> CreateAsync(PostgreSqlFixture fixture)
        {
            var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
            var clock = new FakeTimeProvider(Now);
            var host = GuardHost.Create(db, bus => bus.AddHandler<FirstGuardHandler>().AddHandler<SecondGuardHandler>(), clock: clock);
            return new Rig(host, clock);
        }

        public void Down(string handler, string tenant)
            => _down.GetOrAdd(handler, static _ => new(StringComparer.Ordinal))[tenant] = true;

        public List<string> TakeCalls()
        {
            var taken = new List<string>();
            while (_calls.TryDequeue(out var call))
            {
                taken.Add(call);
            }

            return taken;
        }

        public async Task<EnvelopeReceiveResult> ReceiveAsync(string? tenant) => (await ReceiveWithIdAsync(tenant)).Result;

        public async Task<(Guid Id, EnvelopeReceiveResult Result)> ReceiveWithIdAsync(string? tenant)
        {
            var id = Guid.NewGuid();
            _tenantOf[id] = tenant ?? NoTenant;
            var headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MessageHeaders.CorrelationId] = Guid.NewGuid().ToString(),
            };
            if (tenant is not null)
            {
                headers[MessageHeaders.TenantId] = tenant;
            }

            var envelope = new MessageEnvelope
            {
                Id = id.ToString(),
                Source = GuardHost.ServiceName,
                Type = GuardEvent.WireType,
                Time = Now,
                DataContentType = "application/json",
                Headers = headers,
                Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new GuardEvent(id), JsonSerializerOptions.Web)),
            };

            return (id, await Host.ReceiveAsync(envelope));
        }

        public async Task<InboxMessage> SeedFailedAsync<THandler>(string? tenant)
        {
            var id = Guid.NewGuid();
            _tenantOf[id] = tenant ?? NoTenant;
            var row = new InboxMessage
            {
                MessageId = MessageId.From(id),
                ConsumerType = MessageConsumerKey.Of(typeof(THandler)),
                TenantId = tenant,
                EventType = typeof(GuardEvent).AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(new GuardEvent(id), JsonSerializerOptions.Web),
                Source = GuardHost.ServiceName,
                Status = InboxMessageStatus.Failed,
                RetryCount = 1,
                ReceivedAtUtc = Now.AddSeconds(++_order - 10_000),
                OccurredOnUtc = Now,
            };

            await using var context = Host.NewContext();
            context.InboxMessages.Add(row);
            await context.SaveChangesAsync();
            return row;
        }

        public async Task<List<InboxMessage>> RowsOfAsync(string consumerType)
            => [.. (await Host.InboxAsync()).Where(r => r.ConsumerType == consumerType)];

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Host.Database.DisposeAsync();
        }
    }
}
