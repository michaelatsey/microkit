using MicroKit.Execution.Abstractions;
using MicroKit.Messaging.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// One tenant's failures no longer stall a store's outbox endpoint (#124): a down verdict on a row
/// with a tenant backs off that tenant on the endpoint, the half-open probe tells it from a real
/// outage, and two tenants that never recover cannot hold a third.
/// </summary>
/// <remarks>
/// The real composition — the EF store on PostgreSQL, the store's dispatchers, its back-off singleton
/// — driven one batch at a time on a <see cref="FakeTimeProvider"/>, as the store's loop would at the
/// normal cadence: batches back to back at one instant, then the clock moved to the next due time.
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class TenantBackoffOutboxTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [DockerRequiredFact]
    public async Task V1_AnActivationFailureForOneTenant_DefersOnlyThatTenant_AndNeverBlocksTheEndpoint()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Faults.ActivationFails.Add("A");
        var rows = await rig.SeedAsync(("A", null), ("B", null), ("A", null), ("B", null), ("A", null), ("B", null));

        await rig.CycleAsync();

        var after = await rig.ReadAsync();
        after.Where(r => r.TenantId == "B").ShouldAllBe(r => r.Status == OutboxMessageStatus.Published, "B goes out in the first batch");
        after.Where(r => r.TenantId == "A").ShouldAllBe(r =>
            r.Status == OutboxMessageStatus.Pending && r.RetryCount == 0 && !r.DeadLettered
            && r.NextRetryAtUtc == Now + rig.Options.PollingInterval);
        rig.Faults.ActivationsOf("A").ShouldBe(1, "A's later rows wait unattempted");
        rig.Backoff.BlockedAt(rig.Clock.GetUtcNow()).ShouldBe(OutboxEndpoints.None);
        rows.Count.ShouldBe(6);

        // A never recovers: tried once per its own back-off, which grows to its ceiling; never
        // dead-lettered, no attempt counted, the endpoint never blocked.
        var expected = rig.Options.PollingInterval;
        for (var period = 1; period <= 8; period++)
        {
            await rig.AdvanceToNextDueAsync();
            var activations = rig.Faults.ActivationsOf("A");
            var now = rig.Clock.GetUtcNow();

            await rig.CycleAsync();

            rig.Faults.ActivationsOf("A").ShouldBe(activations + 1, $"period {period}: once per back-off");
            rig.Backoff.BlockedAt(now).ShouldBe(OutboxEndpoints.None, $"period {period}");
            expected = expected * 2 > rig.Options.TransportUnavailableBackoff ? rig.Options.TransportUnavailableBackoff : expected * 2;
            (await rig.ReadAsync()).Where(r => r.TenantId == "A").ShouldAllBe(r =>
                r.Status == OutboxMessageStatus.Pending && r.RetryCount == 0 && !r.DeadLettered
                && r.NextRetryAtUtc == now + expected);
        }
    }

    [DockerRequiredFact]
    public async Task V2_ATransportUnavailableForOneTenant_DefersOnlyThatTenant_OnTheTransportAndOnTheLocalEndpoint()
    {
        await using var rig = await Rig.CreateAsync(fixture, bus => bus.AddLocalConsumerSource(new TenantNoticeConsumers()));
        rig.Faults.TransportFails.Add("A");
        rig.Faults.LocalDependencyFails.Add("A");
        await rig.SeedAsync(("A", null), ("B", null), ("A", OutboxMessage.LocalEndpoint), ("B", OutboxMessage.LocalEndpoint));

        await rig.CycleAsync();

        var rows = await rig.ReadAsync();
        rows.Where(r => r.TenantId == "B").ShouldAllBe(r => r.Status == OutboxMessageStatus.Published,
            "B's transport row and B's local row both go out");
        rows.Where(r => r.TenantId == "A").ShouldAllBe(r =>
            r.Status == OutboxMessageStatus.Pending && r.RetryCount == 0 && !r.DeadLettered
            && r.NextRetryAtUtc == Now + rig.Options.PollingInterval);
        rig.Faults.LocalDelivered.ShouldBe(["B"], "the local consumer handled B's row only");
        rig.Backoff.BlockedAt(Now).ShouldBe(OutboxEndpoints.None, "neither endpoint is down");
        rig.Backoff.TenantsOf(OutboxDispatchers.Transport).IsBlocked("A", Now, out _).ShouldBeTrue();
        rig.Backoff.TenantsOf(OutboxDispatchers.Local).IsBlocked("A", Now, out _).ShouldBeTrue("A3: the local endpoint backs A off, D1");

        // A3: a local delivery's tenant is the outbox's to defer; no inbox row is written for it.
        (await rig.ReadInboxAsync()).ShouldAllBe(r => r.TenantId == "B");
    }

    [DockerRequiredFact]
    public async Task V1b_TwoTenantsThatNeverRecover_OldestInTheQueue_CannotHoldAThird()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Faults.TransportFails.UnionWith(["A", "B"]);
        await rig.SeedAsync(("A", null), ("B", null), ("A", null), ("B", null), ("C", null), ("C", null));

        await rig.CycleAsync();
        rig.Faults.TakeAttempts().ShouldBe(["A", "B"], "A, then the probe B: the endpoint is blocked");
        rig.Backoff.BlockedAt(Now).ShouldBe(OutboxEndpoints.Transport);

        // One endpoint back-off later, C goes first — it is the tenant not known to fail — and out.
        await rig.AdvanceToNextDueAsync();
        await rig.CycleAsync();

        var attempts = rig.Faults.TakeAttempts();
        attempts[0].ShouldBe("C", "the probe falls on a tenant with no back-off entry: the failed probe's tenant has one (A1)");
        (await rig.ReadAsync()).Where(r => r.TenantId == "C").ShouldAllBe(r => r.Status == OutboxMessageStatus.Published);

        for (var period = 0; period < 6; period++)
        {
            await rig.AdvanceToNextDueAsync();
            await rig.CycleAsync();
            rig.Backoff.BlockedAt(rig.Clock.GetUtcNow()).ShouldBe(OutboxEndpoints.None, "A and B hold only themselves");
        }

        (await rig.ReadAsync()).Where(r => r.TenantId != "C").ShouldAllBe(r =>
            r.Status == OutboxMessageStatus.Pending && r.RetryCount == 0 && !r.DeadLettered);
    }

    [DockerRequiredFact]
    public async Task V3_ARealOutage_CostsAtMostTwoAttemptsPerBatch_EvenOnceEveryTenantHasAnEntry_AndRecovers()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Faults.EveryTransportFails = true;
        string[] tenants = ["A", "B", "C", "D", "E", "F", "G", "H"];
        await rig.SeedAsync([.. tenants.Select(t => (t, (string?)null)), .. tenants.Select(t => (t, (string?)null))]);

        var transportTenants = rig.Backoff.TenantsOf(OutboxDispatchers.Transport);
        int? allKnownAt = null;
        var attemptsOnceAllKnown = 0;
        for (var period = 0; ; period++)
        {
            period.ShouldBeLessThan(40, "every tenant gets an entry within a few back-offs");
            var now = rig.Clock.GetUtcNow();

            await rig.CycleAsync();

            var attempts = rig.Faults.TakeAttempts();
            attempts.Count.ShouldBeLessThanOrEqualTo(2, $"period {period}: never one per tenant ({string.Join(", ", attempts)})");
            if (attempts.Count == 2)
            {
                rig.Backoff.BlockedAt(now).ShouldBe(OutboxEndpoints.Transport, $"period {period}: then the endpoint is blocked as in #188");
            }

            (await rig.ReadAsync()).ShouldAllBe(r => r.RetryCount == 0 && !r.DeadLettered && r.Status == OutboxMessageStatus.Pending);

            if (allKnownAt is not null)
            {
                attemptsOnceAllKnown += attempts.Count;
                if (period >= allKnownAt + 2)
                {
                    break;
                }
            }
            else if (tenants.All(t => transportTenants.TryGetDelay(t, out _)))
            {
                allKnownAt = period;
            }

            await rig.AdvanceToNextDueAsync();
        }

        attemptsOnceAllKnown.ShouldBeGreaterThan(0, "control: the periods after every tenant has an entry do attempt rows");

        // The broker comes back: everything drains.
        rig.Faults.EveryTransportFails = false;
        for (var period = 0; (await rig.ReadAsync()).Any(r => r.Status != OutboxMessageStatus.Published); period++)
        {
            period.ShouldBeLessThan(40, "the backlog drains once the broker is back");
            await rig.AdvanceToNextDueAsync();
            await rig.CycleAsync();
        }

        (await rig.ReadAsync()).ShouldAllBe(r => r.RetryCount == 0, "the outage counted no attempt");
    }

    [DockerRequiredFact]
    public async Task AKnownTenantThatRecoversWhileTheEndpointIsSuspected_IsPublished()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Faults.TransportFails.UnionWith(["A", "B", "C"]);
        await rig.SeedAsync(("A", null), ("B", null), ("C", null));

        await rig.CycleAsync();                 // A, then the probe B: the endpoint is blocked.
        await rig.AdvanceToNextDueAsync();
        await rig.CycleAsync();                 // C first (no entry), then A as the probe: blocked again.
        rig.Backoff.TenantsOf(OutboxDispatchers.Transport).TryGetDelay("C", out _).ShouldBeTrue("control: C is a known tenant now");
        rig.Backoff.BlockedAt(rig.Clock.GetUtcNow()).ShouldBe(OutboxEndpoints.Transport, "control: the endpoint is suspected");

        rig.Faults.TransportFails.Remove("C");
        for (var period = 0; (await rig.ReadAsync()).Single(r => r.TenantId == "C").Status != OutboxMessageStatus.Published; period++)
        {
            period.ShouldBeLessThan(6, "C, recovered, must not starve behind A and B");
            await rig.AdvanceToNextDueAsync();
            await rig.CycleAsync();
        }
    }

    // ---------- A7: the claim leaves a tenant in back-off out ----------

    [DockerRequiredFact]
    public async Task V11_AOneTenantStore_TransportDownForReal_ClaimsAndWritesNoneOfItsRows_UntilItsBackOffEnds_OneSendPerPeriod()
    {
        await using var rig = await Rig.CreateAsync(fixture);
        rig.Faults.EveryTransportFails = true;
        var backlog = (rig.Options.BatchSize * 2) + 5;
        await rig.SeedAsync([.. Enumerable.Repeat<(string?, string?)>(("A", null), backlog)]);

        var first = await rig.CycleAsync();
        first.Claimed.ShouldBe(rig.Options.BatchSize, "control: the first claim reaches A's rows");
        rig.Faults.TakeAttempts().ShouldBe(["A"], "A's first row fails; the rest of that batch is deferred unattempted");
        rig.Backoff.TenantsOf(OutboxDispatchers.Transport).IsBlocked("A", Now, out var until).ShouldBeTrue();
        rig.Backoff.BlockedAt(Now).ShouldBe(OutboxEndpoints.None, "one tenant: the probe never fires, the endpoint is never blocked");

        var expected = rig.Options.PollingInterval;
        for (var period = 0; period < 6; period++)
        {
            // Within A's back-off: polled as the loop would, the claim returns none of A's rows and writes
            // none of them — their next attempt, claim token and lease are left as they are.
            var snapshot = Snapshot(await rig.ReadAsync());
            foreach (var step in new[] { TimeSpan.Zero, expected / 4, expected / 2, expected - TimeSpan.FromTicks(1) })
            {
                rig.Clock.SetUtcNow(until - expected + step);
                (await rig.CycleAsync()).Claimed.ShouldBe(0, $"period {period}, +{step}: A is left out of the claim");
            }

            Snapshot(await rig.ReadAsync()).ShouldBe(snapshot, $"period {period}: none of A's rows written while it is excluded");
            rig.Faults.TakeAttempts().ShouldBeEmpty($"period {period}: no send within the back-off");

            // A's back-off over: one send, which fails again; A alone backs off further.
            rig.Clock.SetUtcNow(until);
            (await rig.CycleAsync()).Claimed.ShouldBeGreaterThan(0, $"period {period}: A is claimed again once its back-off ends");
            rig.Faults.TakeAttempts().ShouldBe(["A"], $"period {period}: at most one send per back-off period");
            expected = expected * 2 > rig.Options.TransportUnavailableBackoff ? rig.Options.TransportUnavailableBackoff : expected * 2;
            rig.Backoff.TenantsOf(OutboxDispatchers.Transport).IsBlocked("A", until, out var next).ShouldBeTrue();
            next.ShouldBe(until + expected, $"period {period}: A's own back-off grows");
            until = next;
        }

        (await rig.ReadAsync()).ShouldAllBe(r =>
            r.Status == OutboxMessageStatus.Pending && r.RetryCount == 0 && !r.DeadLettered && r.ClaimToken == null);
    }

    [DockerRequiredFact]
    public async Task V12_WhileATenantIsExcluded_RowsWithNoTenantAndTheOtherTenantsRows_AreClaimedAndPublished()
    {
        await using var rig = await Rig.CreateAsync(fixture, bus => bus.AddLocalConsumerSource(new TenantNoticeConsumers()));
        rig.Faults.TransportFails.Add("A");
        await rig.SeedAsync(("A", null), ("A", null), ("A", OutboxMessage.LocalEndpoint));
        await rig.CycleAsync();
        rig.Faults.TakeAttempts().ShouldBe(["A"]);
        rig.Backoff.TenantsOf(OutboxDispatchers.Transport).IsBlocked("A", Now, out _).ShouldBeTrue("control: A is excluded");
        var aBefore = Snapshot((await rig.ReadAsync()).Where(r => r.TenantId == "A" && r.Endpoint is null));

        // Rows written while A is excluded: B's and rows with no tenant, on both endpoints.
        await rig.SeedAsync(("B", null), (null, null), ("B", OutboxMessage.LocalEndpoint), (null, OutboxMessage.LocalEndpoint));
        rig.Clock.Advance(rig.Options.PollingInterval / 2);
        await rig.CycleAsync();

        var rows = await rig.ReadAsync();
        rows.Where(r => r.TenantId != "A").ShouldAllBe(r => r.Status == OutboxMessageStatus.Published,
            "B's rows and the rows with no tenant are claimed and published");
        rows.Count(r => r.TenantId != "A").ShouldBe(4);
        rig.Faults.TakeAttempts().ShouldBe(["B", null], "the transport is tried for B and for no tenant, never for A");
        Snapshot(rows.Where(r => r.TenantId == "A" && r.Endpoint is null)).ShouldBe(aBefore, "A's transport rows are not written");
        rows.Single(r => r.TenantId == "A" && r.Endpoint is not null).Status.ShouldBe(OutboxMessageStatus.Published,
            "A's local row: A is excluded on the transport only");
    }

    private static string Snapshot(IEnumerable<OutboxMessage> rows) => string.Join(
        ";", rows.OrderBy(r => r.Id).Select(r => $"{r.Id.Value}|{r.Status}|{r.NextRetryAtUtc:O}|{r.ClaimToken}|{r.LockedUntilUtc:O}|{r.RetryCount}"));

    private sealed record TenantNotice;

    private sealed class TenantNoticeConsumers : ILocalConsumerSource
    {
        public IEnumerable<LocalConsumer> GetConsumers() =>
        [
            new LocalConsumer(
                typeof(TenantNotice),
                "tests.tenant-backoff.local",
                typeof(TenantNoticeConsumers),
                static (services, _, context, _) =>
                {
                    services.GetRequiredService<TenantFaults>().Local(context.TenantId);
                    return ValueTask.CompletedTask;
                }),
        ];
    }

    /// <summary>What fails for which tenant, and what was attempted.</summary>
    private sealed class TenantFaults
    {
        private readonly Lock _gate = new();
        private readonly List<string?> _attempts = [];
        private readonly Dictionary<string, int> _activations = new(StringComparer.Ordinal);

        public HashSet<string> ActivationFails { get; } = new(StringComparer.Ordinal);

        public HashSet<string> TransportFails { get; } = new(StringComparer.Ordinal);

        public HashSet<string> LocalDependencyFails { get; } = new(StringComparer.Ordinal);

        public bool EveryTransportFails { get; set; }

        public List<string?> LocalDelivered { get; } = [];

        public ScriptedTransport Transport => new(this);

        public int ActivationsOf(string tenant)
        {
            lock (_gate)
            {
                return _activations.GetValueOrDefault(tenant);
            }
        }

        /// <summary>The tenants of the sends attempted since the last call, in order.</summary>
        public List<string?> TakeAttempts()
        {
            lock (_gate)
            {
                var taken = _attempts.ToList();
                _attempts.Clear();
                return taken;
            }
        }

        public ScriptedTransport Activate(string? tenant)
        {
            lock (_gate)
            {
                _activations[tenant ?? string.Empty] = _activations.GetValueOrDefault(tenant ?? string.Empty) + 1;
            }

            return tenant is not null && ActivationFails.Contains(tenant)
                ? throw new InvalidOperationException($"Tenant '{tenant}' has no transport credentials.")
                : Transport;
        }

        public void Send(string? tenant)
        {
            lock (_gate)
            {
                _attempts.Add(tenant);
            }

            if (EveryTransportFails || (tenant is not null && TransportFails.Contains(tenant)))
            {
                throw new OutboxTransportUnavailableException($"The broker refused tenant '{tenant}'.");
            }
        }

        public void Local(string? tenant)
        {
            if (tenant is not null && LocalDependencyFails.Contains(tenant))
            {
                throw new InboxDependencyUnavailableException($"Tenant '{tenant}''s dependency is down.");
            }

            lock (_gate)
            {
                LocalDelivered.Add(tenant);
            }
        }
    }

    private sealed class ScriptedTransport(TenantFaults faults) : IMessageTransport
    {
        public ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
        {
            faults.Send(envelope.Headers.GetValueOrDefault(MessageHeaders.TenantId));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly StoreDatabase _db;
        private readonly ServiceProvider _provider;
        private int _order;

        private Rig(StoreDatabase db, ServiceProvider provider, FakeTimeProvider clock)
        {
            _db = db;
            _provider = provider;
            Clock = clock;
        }

        public static MessageStoreKey Store { get; } = EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>();

        public FakeTimeProvider Clock { get; }

        public TenantFaults Faults => _provider.GetRequiredService<TenantFaults>();

        public OutboxProcessorOptions Options => _provider.GetRequiredService<OutboxProcessorOptions>();

        public OutboxEndpointBackoff Backoff => _provider.GetRequiredKeyedService<OutboxEndpointBackoff>(Store);

        public static async Task<Rig> CreateAsync(PostgreSqlFixture fixture, Action<MessagingBuilder>? configure = null)
        {
            var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
            var clock = new FakeTimeProvider(Now);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<TenantFaults>();
            services.AddDbContext<TestMessagingDbContext>(o => o.UseNpgsql(db.ConnectionString));

            // Resolved in each row's dispatch scope, as a transport with per-tenant credentials would be.
            services.AddScoped<IMessageTransport>(static sp =>
                sp.GetRequiredService<TenantFaults>().Activate(sp.GetRequiredService<IExecutionContext>().TenantId));
            services.AddMicroKitMessaging(bus =>
            {
                bus.ServiceName = "tests.tenant-backoff";
                bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
                bus.Configure(o =>
                {
                    o.Outbox.RetentionDays = 0;
                    o.Inbox.RetentionDays = 0;
                });
                configure?.Invoke(bus);
            });

            return new Rig(db, services.BuildServiceProvider(), clock);
        }

        public async Task<List<OutboxMessage>> SeedAsync(params (string? Tenant, string? Endpoint)[] rows)
        {
            var seeded = rows.Select(r => new OutboxMessage
            {
                Id = MessageId.New(),
                Endpoint = r.Endpoint,
                ContractName = r.Endpoint is null ? "tests.tenant-backoff.event.v1" : null,
                Source = r.Endpoint is null ? "tests.tenant-backoff" : null,
                TenantId = r.Tenant,
                EventType = typeof(TenantNotice).AssemblyQualifiedName!,
                Payload = "{}",
                Status = OutboxMessageStatus.Pending,
                OccurredOnUtc = Now,
                CreatedAtUtc = Now.AddSeconds(++_order - 10_000),
                CorrelationId = CorrelationId.New(),
            }).ToList();

            await using var context = NewContext();
            context.OutboxMessages.AddRange(seeded);
            await context.SaveChangesAsync();
            return seeded;
        }

        public async Task<OutboxBatchResult> CycleAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredKeyedService<IOutboxProcessor>(Store).ProcessBatchAsync(Options.BatchSize);
        }

        /// <summary>Moves the clock to the next instant a row becomes due, as the loop's polling reaches it.</summary>
        public async Task AdvanceToNextDueAsync()
        {
            var now = Clock.GetUtcNow();
            var next = (await ReadAsync())
                .Where(r => r.Status == OutboxMessageStatus.Pending && r.NextRetryAtUtc > now)
                .Min(r => r.NextRetryAtUtc);
            if (next is { } due)
            {
                Clock.SetUtcNow(due);
            }
        }

        public async Task<List<OutboxMessage>> ReadAsync()
        {
            await using var context = NewContext();
            return await context.OutboxMessages.AsNoTracking().OrderBy(m => m.CreatedAtUtc).ToListAsync();
        }

        public async Task<List<InboxMessage>> ReadInboxAsync()
        {
            await using var context = NewContext();
            return await context.InboxMessages.AsNoTracking().ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _db.DisposeAsync();
        }

        private TestMessagingDbContext NewContext()
            => new(new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(_db.ConnectionString).Options);
    }
}
