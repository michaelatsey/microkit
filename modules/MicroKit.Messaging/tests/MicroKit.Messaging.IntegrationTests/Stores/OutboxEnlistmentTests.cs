using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MicroKit.Execution.Abstractions;

namespace MicroKit.Messaging.IntegrationTests.Stores;

/// <summary>
/// Outbox by enlistment (#188 D5), through the real container on SQLite: with a store configured,
/// <see cref="IMessageBus.PublishAsync{T}"/> adds the row to the scope's <c>DbContext</c> and saves
/// nothing; the caller's save commits it with the business data, with no open-transaction
/// precondition; and a row its scope never saved is reported, never lost in silence.
/// </summary>
/// <remarks>
/// These replace the staging tests of the removed <c>IIntegrationEventPublisher</c>: its "writes
/// inside the caller's transaction" and "a rollback erases it" become "enlists without saving" and
/// "a rollback erases it"; "the writer and the caller share one DbContext" and "the publisher reads
/// the message scope's execution context" are asserted again below.
/// </remarks>
public sealed class OutboxEnlistmentTests
{
    [Fact]
    public Task PublishAsync_EnlistsTheRowWithoutSaving_AndTheCallersSaveCommitsIt()
        => Task.Run(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var id = Guid.NewGuid();

            await using (var scope = fixture.Provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
                context.BusinessRecords.Add(new BusinessRecord { Id = id, Value = "placed" });

                var messageId = await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new EnlistedEvent(id));

                var entry = context.ChangeTracker.Entries<OutboxMessage>().ShouldHaveSingleItem();
                entry.State.ShouldBe(EntityState.Added, "the bus enlists and saves nothing");
                entry.Entity.Id.ShouldBe(messageId, "the caller is returned the row's identity");
                (await fixture.ReadOutboxAsync()).ShouldBeEmpty("nothing reached the database before the save");

                // No transaction is opened: a plain SaveChanges is the whole commit.
                await context.SaveChangesAsync();
            }

            var row = (await fixture.ReadOutboxAsync()).ShouldHaveSingleItem();
            row.Endpoint.ShouldBeNull("a contract is bound for the transport");
            row.ContractName.ShouldBe("tests.enlistment.event.v1");
            row.Source.ShouldBe(Fixture.ServiceName);
            row.Status.ShouldBe(OutboxMessageStatus.Pending);
            (await fixture.ReadRecordsAsync()).ShouldHaveSingleItem().Id.ShouldBe(id);
            fixture.Logs.Entries.ShouldNotContain(e => e.EventId == 4000 || e.EventId == 4001);
        });

    [Fact]
    public Task PublishAsync_WhenTheCallersTransactionRollsBack_LeavesNeitherTheRowNorTheBusinessData()
        => Task.Run(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();

            await using (var scope = fixture.Provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
                await using var transaction = await context.Database.BeginTransactionAsync();

                context.BusinessRecords.Add(new BusinessRecord { Id = Guid.NewGuid(), Value = "placed" });
                await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new EnlistedEvent(Guid.NewGuid()));
                await context.SaveChangesAsync();

                context.ChangeTracker.Entries<OutboxMessage>().ShouldHaveSingleItem()
                    .State.ShouldBe(EntityState.Unchanged, "the save reached the database");

                await transaction.RollbackAsync();
            }

            (await fixture.ReadOutboxAsync()).ShouldBeEmpty();
            (await fixture.ReadRecordsAsync()).ShouldBeEmpty();
        });

    /// <summary>
    /// The row lands in the scope's own <c>DbContext</c> — the instance the caller saves — and
    /// carries the scope's tenant and correlation, read through its execution context.
    /// </summary>
    [Fact]
    public Task PublishAsync_InAMessageScope_EnlistsInTheScopesContextWithItsTenantAndCorrelation()
        => Task.Run(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var correlation = Guid.NewGuid();

            var scope = await fixture.Provider.GetRequiredService<IExecutionScopeFactory>()
                .CreateScopeAsync(new SettlementExecutionContext
                {
                    TenantId = "tenant-a",
                    CorrelationId = correlation.ToString(),
                });
            await using (scope)
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new EnlistedEvent(Guid.NewGuid()));

                var context = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
                context.ChangeTracker.Entries<OutboxMessage>().ShouldHaveSingleItem(
                    "the scope's context is the one the bus enlisted in");
                await context.SaveChangesAsync();
            }

            var row = (await fixture.ReadOutboxAsync()).ShouldHaveSingleItem();
            row.TenantId.ShouldBe("tenant-a");
            row.CorrelationId.ShouldBe(CorrelationId.From(correlation));
        });

    /// <summary>V9: a row enlisted and never saved is reported at <c>Error</c>, with its own event.</summary>
    [Fact]
    public Task AnEnlistedRowItsScopeNeverSaved_IsReportedAsLost()
        => Task.Run(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            MessageId id;

            await using (var scope = fixture.Provider.CreateAsyncScope())
            {
                id = await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new EnlistedEvent(Guid.NewGuid()));
            }

            var lost = fixture.Logs.Entries.Where(e => e.EventId == 4000).ShouldHaveSingleItem();
            lost.Level.ShouldBe(LogLevel.Error);
            lost.RenderedMessage.ShouldContain(id.Value.ToString());
            lost.RenderedMessage.ShouldContain("tests.enlistment.event.v1");
            lost.RenderedMessage.ShouldContain(EntityFrameworkOutbox.KeyOf<TestMessagingDbContext>().Value);
            (await fixture.ReadOutboxAsync()).ShouldBeEmpty();
        });

    /// <summary>
    /// A row discarded with the change tracker went with the work it announced: <c>Debug</c>, never
    /// the lost-row <c>Error</c>.
    /// </summary>
    [Fact]
    public Task AnEnlistedRowDiscardedWithTheTracker_IsReportedAtDebugOnly()
        => Task.Run(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();

            await using (var scope = fixture.Provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new EnlistedEvent(Guid.NewGuid()));
                scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().ChangeTracker.Clear();
            }

            fixture.Logs.Entries.ShouldNotContain(e => e.EventId == 4000);
            fixture.Logs.Entries.Where(e => e.EventId == 4001).ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Debug);
        });

    private sealed class Fixture : IAsyncDisposable
    {
        internal const string ServiceName = "tests.enlistment";
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, ServiceProvider provider, CapturingLoggerProvider logs)
        {
            _connection = connection;
            Provider = provider;
            Logs = logs;
        }

        public ServiceProvider Provider { get; }

        public CapturingLoggerProvider Logs { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var logs = new CapturingLoggerProvider();

            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
            services.AddDbContext<TestMessagingDbContext>(o => o.UseSqlite(connection));
            services.AddMicroKitMessaging(bus =>
            {
                bus.ServiceName = ServiceName;
                bus.Publishes<EnlistedEvent>();
                bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
                bus.Services.AddSingleton(NSubstitute.Substitute.For<IMessageTransport>());
            });

            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().Database.EnsureCreatedAsync();
            }

            return new Fixture(connection, provider, logs);
        }

        public async Task<List<OutboxMessage>> ReadOutboxAsync()
        {
            await using var probe = Probe();
            return await probe.OutboxMessages.AsNoTracking().ToListAsync();
        }

        public async Task<List<BusinessRecord>> ReadRecordsAsync()
        {
            await using var probe = Probe();
            return await probe.BusinessRecords.AsNoTracking().ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private TestMessagingDbContext Probe()
            => new(new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(_connection).Options);
    }
}

[MessageContract("tests.enlistment.event")]
internal sealed record EnlistedEvent(Guid Id) : IIntegrationEvent;
