using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using MicroKit.Execution.Abstractions;

namespace MicroKit.Messaging.IntegrationTests.Stores;

using MicroKit.Messaging.Publishing;

/// <summary>
/// The properties that cannot be unit-tested: "the publisher never commits on its own" and "the
/// guard is meaningful" can only be demonstrated against a real unit of work.
/// </summary>
/// <remarks>
/// These go through the <b>real container</b>, not a hand-constructed publisher. The guard rests on
/// the writer and the caller resolving the same <c>DbContext</c>, which is a composition property,
/// so it has to be asserted against a composed container.
/// </remarks>
public sealed class IntegrationEventStagingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 14, 22, TimeSpan.Zero);

    /// <summary>
    /// <b>The single most important test on this path.</b> If the writer ever gains a
    /// <c>SaveChangesAsync</c> — added one day "so the row is definitely written" — this is what
    /// fails, and nothing else would.
    /// </summary>
    /// <remarks>
    /// It asserts the change-tracker state rather than a row count after a rollback. A rollback
    /// test passes either way: if the writer had committed inside the ambient transaction, the
    /// rollback would undo that too, and the assertion would be vacuous. An entry still in
    /// <see cref="EntityState.Added"/> is positive evidence that nothing was written — EF resets it
    /// to <c>Unchanged</c> the moment a save succeeds. Same reasoning
    /// <c>IInboxSettlementStore.IsMarkUncommitted</c> uses.
    /// </remarks>
    [Fact]
    public Task PublishAsync_StagesTheRow_WithoutWritingIt()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();
            await using var scope = fixture.Provider.CreateAsyncScope();

            var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

            await using var transaction = await context.Database.BeginTransactionAsync();

            await publisher.PublishAsync(new StagingTestEvent(Guid.NewGuid()));

            var entry = context.ChangeTracker
                .Entries<IntegrationEventMessage>()
                .ShouldHaveSingleItem();

            entry.State.ShouldBe(
                EntityState.Added,
                "the publisher stages; the caller's unit of work owns the commit");

            await transaction.RollbackAsync();
        });

    /// <summary>
    /// The guarantee itself: a rollback un-publishes the event, which is the point of staging into
    /// the caller's transaction rather than a transaction of the publisher's own.
    /// </summary>
    /// <remarks>
    /// Distinct from the test above, and weaker on purpose. This one calls
    /// <c>SaveChangesAsync</c> — exactly as a real handler's <c>CommitAsync</c> does — so it
    /// asserts the end-to-end property rather than the writer's internal restraint.
    /// </remarks>
    [Fact]
    public Task PublishAsync_WhenTheTransactionRollsBack_LeavesNoIntegrationEvent()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();

            await using (var scope = fixture.Provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

                await using var transaction = await context.Database.BeginTransactionAsync();

                await publisher.PublishAsync(new StagingTestEvent(Guid.NewGuid()));
                await context.SaveChangesAsync();

                await transaction.RollbackAsync();
            }

            (await fixture.ReadAllAsync()).ShouldBeEmpty();
        });

    [Fact]
    public Task PublishAsync_WhenTheTransactionCommits_PersistsTheRow()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();
            var constatId = Guid.NewGuid();
            var occurredOn = Now.AddHours(-3);

            MessageId id;
            await using (var scope = fixture.Provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

                await using var transaction = await context.Database.BeginTransactionAsync();

                id = await publisher.PublishAsync(new StagingTestEvent(constatId), occurredOn);
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            var row = (await fixture.ReadAllAsync()).ShouldHaveSingleItem();

            row.Id.ShouldBe(id);
            row.ContractName.ShouldBe(StagingTestEvent.ContractName);
            row.Source.ShouldBe(ComposedFixture.Source);
            row.Status.ShouldBe(IntegrationEventStatus.Pending);
            row.DeadLettered.ShouldBeFalse();
            row.RetryCount.ShouldBe(0);
            row.CreatedAtUtc.ShouldBe(Now);
            row.OccurredOnUtc.ShouldBe(occurredOn);
            row.Data.ShouldContain(constatId.ToString());
        });

    /// <summary>
    /// <b>The scope-identity assumption, asserted rather than commented.</b>
    /// </summary>
    /// <remarks>
    /// <c>HasOpenTransaction</c> reads the transaction on the <c>DbContext</c> the <i>writer</i>
    /// holds. If a caller opened its unit of work on a different instance, the guard would pass
    /// while the row committed somewhere else — the same assumption
    /// <c>IInboxSettlementStore</c> rests on, and the one that produced two blockers in that lot.
    /// <para>
    /// Asserted behaviourally rather than by comparing references: a transaction begun on the
    /// context resolved from this scope must be visible to the writer resolved from the same scope,
    /// and a row staged through the writer must appear in that context's change tracker. Both are
    /// false the moment the two stop being one instance, and neither depends on reaching into the
    /// writer's private state.
    /// </para>
    /// </remarks>
    [Fact]
    public Task TheWriterAndTheCallersUnitOfWorkShareOneDbContext()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();
            await using var scope = fixture.Provider.CreateAsyncScope();

            var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<IIntegrationEventWriter>();

            writer.HasOpenTransaction.ShouldBeFalse("no transaction has been opened yet");

            await using var transaction = await context.Database.BeginTransactionAsync();

            writer.HasOpenTransaction.ShouldBeTrue(
                "the writer must observe a transaction opened on the caller's context");

            await writer.AddAsync(new IntegrationEventMessage
            {
                Id = MessageId.New(),
                ContractName = StagingTestEvent.ContractName,
                Source = ComposedFixture.Source,
                Data = "{}",
                CreatedAtUtc = Now,
                Status = IntegrationEventStatus.Pending,
            });

            context.ChangeTracker.Entries<IntegrationEventMessage>().ShouldHaveSingleItem();

            await transaction.RollbackAsync();
        });

    [Fact]
    public Task PublishAsync_WithNoOpenTransaction_ThrowsThroughTheRealContainer()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();
            await using var scope = fixture.Provider.CreateAsyncScope();

            var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

            await Should.ThrowAsync<IntegrationEventPublishException>(
                async () => await publisher.PublishAsync(new StagingTestEvent(Guid.NewGuid())));

            (await fixture.ReadAllAsync()).ShouldBeEmpty();
        });

    /// <summary>
    /// The publisher resolved from a message scope must see that message's execution context.
    /// </summary>
    /// <remarks>
    /// This is the assertion L0 finding #21 made impossible. The publisher takes
    /// <c>IExecutionContext</c> by <b>constructor</b>, and before the scoped holder existed the
    /// container handed constructor parameters the default registration — a fresh
    /// <c>CorrelationId</c> with a null <c>TenantId</c> — no matter what context the scope was
    /// created with. Every staged row would have carried a null tenant and a fabricated
    /// correlation id, on the two fields that make a row traceable and isolated.
    /// </remarks>
    [Fact]
    public Task ThePublisherReadsTheMessageScopesExecutionContext()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();
            var correlationId = Guid.NewGuid();

            await using (var scope = await fixture.CreateMessageScopeAsync(
                tenantId: "org_7f3a", correlationId: correlationId))
            {
                var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

                await using var transaction = await context.Database.BeginTransactionAsync();

                await publisher.PublishAsync(new StagingTestEvent(Guid.NewGuid()));
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            var row = (await fixture.ReadAllAsync()).ShouldHaveSingleItem();

            row.TenantId.ShouldBe("org_7f3a");
            row.CorrelationId!.Value.ShouldBe(correlationId);
        });

    /// <summary>
    /// <c>ClaimToken</c> must be mapped as an EF concurrency token.
    /// </summary>
    /// <remarks>
    /// The relay that consumes it is a later lot, so nothing exercises the mechanism yet — which is
    /// exactly why this assertion exists now. Without the mapping, the <c>UPDATE</c> a settlement
    /// emits carries only the primary key, and a relay whose lease expired mid-delivery overwrites
    /// the relay that legitimately took the message over. Removing the mapping breaks no test that
    /// exercises concurrency, because there is no such test to break: this one reads the model
    /// instead.
    /// </remarks>
    [Fact]
    public Task TheClaimTokenIsMappedAsAConcurrencyToken()
        => Task.Run(async () =>
        {
            await using var fixture = await ComposedFixture.CreateAsync();
            await using var scope = fixture.Provider.CreateAsyncScope();

            var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();

            context.Model
                .FindEntityType(typeof(IntegrationEventMessage))
                .ShouldNotBeNull()
                .FindProperty(nameof(IntegrationEventMessage.ClaimToken))
                .ShouldNotBeNull()
                .IsConcurrencyToken
                .ShouldBeTrue();
        });

    /// <summary>One composed container over one isolated SQLite connection.</summary>
    private sealed class ComposedFixture : IAsyncDisposable
    {
        internal const string Source = "/microkit/staging-tests";

        private readonly SqliteConnection _connection;

        private ComposedFixture(SqliteConnection connection, ServiceProvider provider)
        {
            _connection = connection;
            Provider = provider;
        }

        public ServiceProvider Provider { get; }

        public static async Task<ComposedFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
            services.AddDbContext<TestMessagingDbContext>(o => o.UseSqlite(connection));

            services.AddIntegrationEventContracts(Source, e => e.Publishes<StagingTestEvent>());

            services.AddMicroKitMessaging()
                .AddIntegrationEventPublishing()
                .AddEfCoreIntegrationEvents<TestMessagingDbContext>();

            var provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true });

            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>()
                    .Database.EnsureCreatedAsync();
            }

            return new ComposedFixture(connection, provider);
        }

        /// <summary>Creates a scope exactly as the message pipeline does.</summary>
        public ValueTask<IExecutionScope> CreateMessageScopeAsync(
            string? tenantId, Guid? correlationId = null)
            => Provider.GetRequiredService<IExecutionScopeFactory>()
                .CreateScopeAsync(new SettlementExecutionContext
                {
                    TenantId = tenantId,
                    CorrelationId = correlationId?.ToString(),
                });

        /// <summary>Reads through a fresh context so no tracked state can colour the result.</summary>
        public async Task<List<IntegrationEventMessage>> ReadAllAsync()
        {
            await using var probe = new TestMessagingDbContext(
                new DbContextOptionsBuilder<TestMessagingDbContext>()
                    .UseSqlite(_connection).Options);

            return await probe.Set<IntegrationEventMessage>().AsNoTracking().ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

[IntegrationEvent(ContractName)]
internal sealed record StagingTestEvent(Guid ConstatId) : IIntegrationEvent
{
    internal const string ContractName = "microkit.test.staging-event.v1";
}
