using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using MicroKit.Execution.Abstractions;

namespace MicroKit.Messaging.IntegrationTests.Stores;

/// <summary>
/// The guarantee the inbox guard exists to provide (#188 D6): the handler's row and its database
/// side effects commit together, in the handler's own unit of work.
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

    private static readonly MessageStoreKey Store = EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>();

    /// <summary>
    /// The guard and the handler must resolve the <b>same</b> <c>DbContext</c> from an attempt's
    /// scope — otherwise the attempt's commit does not carry the handler's writes, and the row and
    /// the work no longer commit together.
    /// </summary>
    /// <remarks>
    /// Asserted behaviourally rather than by comparing references: a write through the handler's
    /// context, then the guard's commit, must persist both the row and the write.
    /// </remarks>
    [Fact]
    public Task TheGuardAndTheHandlerScopeShareOneUnitOfWork()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();
            var key = await fixture.SeedAsync();
            var token = await fixture.ClaimAsync();

            await using var scope = await fixture.CreateMessageScopeAsync();
            var guard = scope.ServiceProvider.GetRequiredKeyedService<IInboxGuardStore>(Store);
            var handlerContext = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();

            await guard.BeginAsync();
            (await guard.MarkClaimedProcessedAsync(key, token)).ShouldBeTrue();
            handlerContext.BusinessRecords.Add(new BusinessRecord { Id = Guid.NewGuid(), Value = "the handler's work" });
            await guard.CommitAsync();

            var row = await fixture.ReadRowAsync();
            row.Status.ShouldBe(InboxMessageStatus.Processed);
            row.ClaimToken.ShouldBeNull("a committed success clears the token");
            (await fixture.CountBusinessRecordsAsync()).ShouldBe(1, "the handler's write committed with the row");
        });

    /// <summary>
    /// C6, kept from the deleted settlement store: the guarantee must not depend on a setting the
    /// library cannot control. <c>QueryTrackingBehavior.NoTracking</c> is an ordinary default on a
    /// read-heavy application's <c>DbContext</c> — and the guard runs on the consumer's context.
    /// </summary>
    /// <remarks>
    /// Confirmed by observation when the settlement store had it: the row came back untracked, the
    /// mutations reached nothing, the save affected 0 rows, and the row replayed forever.
    /// </remarks>
    [Fact]
    public Task MarkClaimedProcessedAsync_PersistsTheMark_WhenTheContextDefaultsToNoTracking()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync(noTracking: true);
            var key = await fixture.SeedAsync();
            var token = await fixture.ClaimAsync();

            await using var scope = await fixture.CreateMessageScopeAsync();
            var guard = scope.ServiceProvider.GetRequiredKeyedService<IInboxGuardStore>(Store);

            await guard.BeginAsync();
            (await guard.MarkClaimedProcessedAsync(key, token)).ShouldBeTrue();
            await guard.CommitAsync();

            var row = await fixture.ReadRowAsync();
            row.Status.ShouldBe(InboxMessageStatus.Processed);
            row.ClaimToken.ShouldBeNull();
        });

    /// <summary>
    /// The same contract end to end, through the store's processor as its loop resolves it: what
    /// the batch REPORTS must match what persisted, under a NoTracking default.
    /// </summary>
    [Fact]
    public Task ProcessBatch_SettlesTheRow_WhenTheContextDefaultsToNoTracking()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync(noTracking: true);
            await fixture.SeedAsync();

            await using var batchScope = fixture.Provider.CreateAsyncScope();
            var result = await batchScope.ServiceProvider
                .GetRequiredKeyedService<IInboxProcessor>(Store)
                .ProcessBatchAsync(100);

            result.Claimed.ShouldBe(1);
            result.Processed.ShouldBe(1);
            fixture.Handled.Count.ShouldBe(1);

            var row = await fixture.ReadRowAsync();
            row.Status.ShouldBe(InboxMessageStatus.Processed);
            row.ClaimToken.ShouldBeNull();
            row.ProcessedAtUtc.ShouldNotBeNull();
        });

    /// <summary>
    /// A handler that clears the tracker mid-run — ordinary in a batch-processing handler — throws
    /// the staged row away. The commit must still write it, or the attempt reports success over a
    /// row that stays claimed and replays on every lease expiry.
    /// </summary>
    [Fact]
    public Task CommitAsync_WritesTheRow_WhenTheHandlerClearedTheTracker()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();
            var key = await fixture.SeedAsync();
            var token = await fixture.ClaimAsync();

            await using var scope = await fixture.CreateMessageScopeAsync();
            var guard = scope.ServiceProvider.GetRequiredKeyedService<IInboxGuardStore>(Store);
            var handlerContext = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();

            await guard.BeginAsync();
            await guard.MarkClaimedProcessedAsync(key, token);
            handlerContext.ChangeTracker.Clear();
            await guard.CommitAsync();

            var row = await fixture.ReadRowAsync();
            row.Status.ShouldBe(InboxMessageStatus.Processed, "the discarded mark was put back");
            row.ClaimToken.ShouldBeNull();
        });

    /// <summary>
    /// The other half: a handler that saved, then cleared the tracker, already wrote the row in the
    /// attempt's transaction — writing it a second time would fail the claim it just satisfied.
    /// </summary>
    [Fact]
    public Task CommitAsync_DoesNotWriteTheRowTwice_WhenTheHandlerSavedThenClearedTheTracker()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();
            var key = await fixture.SeedAsync();
            var token = await fixture.ClaimAsync();

            await using var scope = await fixture.CreateMessageScopeAsync();
            var guard = scope.ServiceProvider.GetRequiredKeyedService<IInboxGuardStore>(Store);
            var handlerContext = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();

            await guard.BeginAsync();
            await guard.MarkClaimedProcessedAsync(key, token);
            await handlerContext.SaveChangesAsync();
            handlerContext.ChangeTracker.Clear();
            await guard.CommitAsync();

            (await fixture.ReadRowAsync()).Status.ShouldBe(InboxMessageStatus.Processed);
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

            // No IOutboxDispatcher is registered, and none is needed: this suite drives the
            // store's IInboxProcessor directly and seeds its own inbox rows rather than going through
            // IEnvelopeReceiver. That keeps the drain's guarantees tested independently of how the
            // rows got there.
            services.AddMicroKitMessaging(bus =>
            {
                bus.ServiceName = "tests.settlement";
                bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
                bus.AddHandler<RecordingSettlementHandler>();
            });

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
                ConsumerType = MessageConsumerKey.Of(typeof(RecordingSettlementHandler)),
                TenantId = "tenant-a",
                EventType = typeof(SettlementTestEvent).AssemblyQualifiedName!,
                Payload = """{"MessageId":{"Value":"8a1d0f7e-1111-2222-3333-444455556666"},"TenantId":"tenant-a","OccurredOnUtc":"2026-08-21T12:00:00+00:00"}""",
                Status = InboxMessageStatus.Failed,
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
                .GetRequiredKeyedService<IInboxProcessorStore>(EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>())
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

        public async Task<int> CountBusinessRecordsAsync()
        {
            await using var probe = new TestMessagingDbContext(
                new DbContextOptionsBuilder<TestMessagingDbContext>()
                    .UseSqlite(_connection).Options);

            return await probe.BusinessRecords.AsNoTracking().CountAsync();
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
// A handled type carries its contract (#187): the composition is rejected otherwise.
[MessageContract("tests.settlement.settlement-test")]
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
    public ValueTask HandleAsync(SettlementTestEvent message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Record();
        return ValueTask.CompletedTask;
    }
}
