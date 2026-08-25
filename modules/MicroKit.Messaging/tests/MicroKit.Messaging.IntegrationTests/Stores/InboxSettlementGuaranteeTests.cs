using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using MicroKit.Execution.Abstractions;

namespace MicroKit.Messaging.IntegrationTests.Stores;

/// <summary>
/// The guarantee ADR-MSG-017 exists to provide: the processed mark and the handler's database
/// side effects commit together, through the handler's own unit of work.
/// </summary>
/// <remarks>
/// <para>
/// These tests go through the <b>real container</b>, not a hand-constructed store. That is the
/// point: every other inbox test builds <c>EfInboxStore</c> directly with an explicit
/// <c>DbContext</c>, which proves the store's behaviour and proves nothing about whether DI hands
/// the settlement store and the handler the same context. The guarantee is a composition
/// property, so it has to be asserted against a composed container.
/// </para>
/// <para>
/// Two of the three failure modes below were live defects found in review and confirmed by
/// observation before being fixed. Both were silent: the batch reported the row
/// <c>Processed</c> while the row stayed claimed, so it replayed on every pass, never incremented
/// <c>RetryCount</c>, and never dead-lettered.
/// </para>
/// </remarks>
public sealed class InboxSettlementGuaranteeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The settlement store and the handler must resolve the <b>same</b> <c>DbContext</c> from the
    /// per-message execution scope — otherwise the handler's commit does not carry the staged
    /// mark, and the transactional guarantee degrades to at-least-once.
    /// </summary>
    /// <remarks>
    /// Asserted behaviourally rather than by comparing references: staging through
    /// <see cref="IInboxSettlementStore"/> and then saving through the <b>handler's</b> context
    /// must persist the mark. If the two contexts differed, the save would write zero rows — which
    /// is precisely the degradation, and is what this asserts against.
    /// </remarks>
    [Fact]
    public Task TheSettlementStoreAndTheHandlerScopeShareOneUnitOfWork()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();
            var key = await fixture.SeedAsync();
            var token = await fixture.ClaimAsync();

            // The per-message scope, created exactly as InboxProcessor creates it.
            await using var scope = await fixture.CreateMessageScopeAsync();

            var settlement = scope.ServiceProvider.GetRequiredService<IInboxSettlementStore>();
            var handlerContext = scope.ServiceProvider
                .GetRequiredService<TestMessagingDbContext>();

            (await settlement.StageProcessedAsync(key, token)).ShouldBeTrue();
            settlement.IsMarkUncommitted(key).ShouldBeTrue("staged, not yet committed");

            var affected = await handlerContext.SaveChangesAsync();

            affected.ShouldBe(1, "the handler's unit of work must carry the staged mark");
            settlement.IsMarkUncommitted(key).ShouldBeFalse();

            var row = await fixture.ReadRowAsync();
            row.Status.ShouldBe(InboxMessageStatus.Processed);
            row.ClaimToken.ShouldBeNull("a committed success clears the token");
        });

    /// <summary>
    /// The guarantee must not depend on a setting the library cannot control.
    /// <c>QueryTrackingBehavior.NoTracking</c> is an ordinary, widely recommended default on a
    /// read-heavy application's <c>DbContext</c> — and this store runs on the consumer's context.
    /// </summary>
    /// <remarks>
    /// Confirmed by observation before the fix: the row came back untracked, the mutations reached
    /// nothing, <c>SaveChanges</c> affected 0 rows, and <c>IsMarkUncommitted</c> returned
    /// <see langword="false"/> — reporting a mark that had never been written as committed.
    /// </remarks>
    [Fact]
    public Task StageProcessedAsync_PersistsTheMark_WhenTheContextDefaultsToNoTracking()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync(noTracking: true);
            var key = await fixture.SeedAsync();
            var token = await fixture.ClaimAsync();

            await using var scope = await fixture.CreateMessageScopeAsync();
            var settlement = scope.ServiceProvider.GetRequiredService<IInboxSettlementStore>();
            var handlerContext = scope.ServiceProvider
                .GetRequiredService<TestMessagingDbContext>();

            (await settlement.StageProcessedAsync(key, token)).ShouldBeTrue();
            settlement.IsMarkUncommitted(key).ShouldBeTrue();

            (await handlerContext.SaveChangesAsync()).ShouldBe(1);

            var row = await fixture.ReadRowAsync();
            row.Status.ShouldBe(InboxMessageStatus.Processed);
            row.ClaimToken.ShouldBeNull();
        });

    /// <summary>
    /// The same contract end to end, through the coordinator. Before the fix this pass reported
    /// <c>Processed = 1</c> while leaving the row <c>Processing</c> with its claim token intact —
    /// success reported over an infinite replay.
    /// </summary>
    [Fact]
    public Task ProcessBatch_SettlesTheRow_WhenTheContextDefaultsToNoTracking()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync(noTracking: true);
            await fixture.SeedAsync();

            await using var batchScope = fixture.Provider.CreateAsyncScope();
            var result = await batchScope.ServiceProvider
                .GetRequiredService<IInboxCoordinator>()
                .ExecuteAsync();

            result.Claimed.ShouldBe(1);
            result.Processed.ShouldBe(1);
            fixture.Handled.Count.ShouldBe(1);

            // The half that was false before: what the batch REPORTS must match what persisted.
            var row = await fixture.ReadRowAsync();
            row.Status.ShouldBe(InboxMessageStatus.Processed);
            row.ClaimToken.ShouldBeNull();
            row.ProcessedAtUtc.ShouldNotBeNull();
        });

    /// <summary>
    /// A discarded staged mark must be reported as <b>uncommitted</b>. Absent means unknown, and
    /// unknown has to fail toward a redundant deferred write rather than toward a lost mark.
    /// </summary>
    /// <remarks>
    /// A handler that calls <c>ChangeTracker.Clear()</c> mid-run — ordinary in batch-processing
    /// handlers — throws the staged mark away. Nothing is pending afterwards, which is exactly why
    /// "nothing pending" cannot be read as "committed": the write was discarded, not saved.
    /// </remarks>
    [Fact]
    public Task IsMarkUncommitted_ReportsUncommitted_WhenTheStagedMarkWasDiscarded()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();
            var key = await fixture.SeedAsync();
            var token = await fixture.ClaimAsync();

            await using var scope = await fixture.CreateMessageScopeAsync();
            var settlement = scope.ServiceProvider.GetRequiredService<IInboxSettlementStore>();
            var handlerContext = scope.ServiceProvider
                .GetRequiredService<TestMessagingDbContext>();

            await settlement.StageProcessedAsync(key, token);
            handlerContext.ChangeTracker.Clear();

            settlement.IsMarkUncommitted(key).ShouldBeTrue(
                "the mark was thrown away, not saved — reporting it committed strands the row");

            var row = await fixture.ReadRowAsync();
            row.Status.ShouldBe(InboxMessageStatus.Processing, "nothing was persisted");
        });

    /// <summary>One composed container over one isolated SQLite connection.</summary>
    private sealed class ComposedFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ComposedFixture(SqliteConnection connection, ServiceProvider provider,
            InvocationRecorder handled)
        {
            _connection = connection;
            Provider = provider;
            Handled = handled;
        }

        public ServiceProvider Provider { get; }

        public InvocationRecorder Handled { get; }

        public static async Task<ComposedFixture> CreateAsync(bool noTracking = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var handled = new InvocationRecorder();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(handled);
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
            services.AddDbContext<TestMessagingDbContext>(o =>
            {
                o.UseSqlite(connection);
                if (noTracking)
                {
                    o.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
                }
            });

            // No IOutboxDispatcher is registered, and none is needed: this suite drives
            // IInboxCoordinator directly and seeds its own inbox rows. AddMessageHandler is still
            // legitimate here for the same reason — InboxIngestionValidator only fails a host that
            // actually starts, and nothing below builds one (ADR-MSG-019).
            services.AddMicroKitMessaging()
                .AddEfCoreOutbox<TestMessagingDbContext>()
                .AddMessageHandler<RecordingSettlementHandler, SettlementTestEvent>();

            var provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true });

            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>()
                    .Database.EnsureCreatedAsync();
            }

            return new ComposedFixture(connection, provider, handled);
        }

        public async Task<InboxMessageKey> SeedAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();

            var row = new InboxMessage
            {
                RowId = Guid.NewGuid(),
                MessageId = MessageId.New(),
                ConsumerType = typeof(RecordingSettlementHandler).AssemblyQualifiedName!,
                TenantId = "tenant-a",
                EventType = typeof(SettlementTestEvent).AssemblyQualifiedName!,
                Payload = """{"MessageId":{"Value":"8a1d0f7e-1111-2222-3333-444455556666"},"TenantId":"tenant-a","OccurredOnUtc":"2026-08-21T12:00:00+00:00"}""",
                Status = InboxMessageStatus.Received,
                ReceivedAtUtc = Now,
            };

            context.InboxMessages.Add(row);
            await context.SaveChangesAsync();

            return new InboxMessageKey(row.MessageId, row.ConsumerType);
        }

        public async Task<Guid> ClaimAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            var claim = await scope.ServiceProvider
                .GetRequiredService<IInboxProcessorStore>()
                .ClaimBatchAsync(10, Lease);

            claim.Count.ShouldBe(1);
            return claim.Token;
        }

        public ValueTask<IExecutionScope> CreateMessageScopeAsync()
            => Provider.GetRequiredService<IExecutionScopeFactory>()
                .CreateScopeAsync(new SettlementExecutionContext { TenantId = "tenant-a" });

        public async Task<InboxMessage> ReadRowAsync()
        {
            await using var probe = new TestMessagingDbContext(
                new DbContextOptionsBuilder<TestMessagingDbContext>()
                    .UseSqlite(_connection).Options);

            return await probe.InboxMessages.AsNoTracking().SingleAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

/// <summary>A public <see cref="IExecutionContext"/> — the Core implementation is internal.</summary>
internal sealed class SettlementExecutionContext : IExecutionContext
{
    public string? TenantId { get; init; }

    public string? CorrelationId { get; init; }

    public string? CausationId { get; init; }

    public IReadOnlyDictionary<string, object?> Properties { get; } =
        new Dictionary<string, object?>();
}

// A bare marker (ADR-MSG-018). The seeded payload below still carries the old fields as JSON;
// deserialization ignores members the type no longer declares, so the fixture is unaffected.
internal sealed record SettlementTestEvent : IIntegrationEvent;

internal sealed class InvocationRecorder
{
    private int _count;

    public int Count => _count;

    public void Record() => Interlocked.Increment(ref _count);
}

internal sealed class RecordingSettlementHandler(InvocationRecorder recorder)
    : IMessageHandler<SettlementTestEvent>
{
    public ValueTask HandleAsync(SettlementTestEvent evt, CancellationToken ct = default)
    {
        recorder.Record();
        return ValueTask.CompletedTask;
    }
}
