using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.IntegrationTests.Stores;

/// <summary>
/// Ingestion tests for the defect where <c>AddAsync</c> reported its nominal outcome — "already
/// present, nothing done" — by throwing.
/// </summary>
/// <remarks>
/// <para>
/// Three sources previously documented a contract nobody implemented: the XML docs on
/// <c>EfInboxStore.AddAsync</c> and <c>IInboxStore.AddAsync</c> both said the caller must catch
/// the duplicate, and a unit-test comment claimed the store absorbed it. The integration test
/// was the only honest one — it asserted the exception escaped, and it was right.
/// </para>
/// <para>
/// That test was <b>inverted, not deleted</b> (see
/// <c>MicroKit.Messaging.MediatR.IntegrationTests.InboxRedeliveryTests</c>). It is the test that
/// caught the defect, and inverted it is what stops the regression.
/// </para>
/// </remarks>
public sealed class InboxIngestionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] FanOutConsumers =
        ["FirstHandler", "SecondHandler", "ThirdHandler"];

    [Fact]
    public Task First_delivery_inserts_the_row()
        => Task.Run(async () =>
        {
            await using var fixture = InboxIngestionFixture.Create();

            var result = await fixture.Store.AddAsync(
                InboxIngestionFixture.NewMessage(consumerType: "OrderPlacedHandler"));

            result.ShouldBe(InboxWriteResult.Added);
            (await fixture.CountAsync()).ShouldBe(1);
        });

    /// <summary>
    /// The inverted contract. A redelivery needs no failure at all — an expired lease after a
    /// crash is enough — so reporting it as an exception made the nominal path an error.
    /// </summary>
    [Fact]
    public Task Redelivery_is_reported_as_already_present_and_does_not_throw()
        => Task.Run(async () =>
        {
            await using var fixture = InboxIngestionFixture.Create();
            var message = InboxIngestionFixture.NewMessage(consumerType: "OrderPlacedHandler");

            await fixture.Store.AddAsync(message);
            var result = await fixture.Store.AddAsync(InboxIngestionFixture.Clone(message));

            result.ShouldBe(InboxWriteResult.AlreadyPresent);
            (await fixture.CountAsync()).ShouldBe(1);
        });

    /// <summary>
    /// The context-poisoning guard. A rejected entity left tracked as added would make the very
    /// next save retry the same failing insert, so a duplicate would break every later write on
    /// that context rather than just its own.
    /// </summary>
    [Fact]
    public Task Context_remains_usable_after_a_deduplicated_write()
        => Task.Run(async () =>
        {
            await using var fixture = InboxIngestionFixture.Create();
            var message = InboxIngestionFixture.NewMessage(consumerType: "OrderPlacedHandler");

            await fixture.Store.AddAsync(message);
            await fixture.Store.AddAsync(InboxIngestionFixture.Clone(message));

            var other = await fixture.Store.AddAsync(
                InboxIngestionFixture.NewMessage(consumerType: "InvoiceIssuedHandler"));

            other.ShouldBe(InboxWriteResult.Added);
            (await fixture.CountAsync()).ShouldBe(2);
        });

    /// <summary>
    /// The multi-consumer loop. One message fans out to one row per consumer; a duplicate on
    /// consumer 2 previously aborted the loop, so consumers 3..N never got their row at all — a
    /// partial redelivery turning into permanent loss for the later consumers.
    /// </summary>
    [Fact]
    public Task A_duplicate_for_one_consumer_does_not_block_the_others()
        => Task.Run(async () =>
        {
            await using var fixture = InboxIngestionFixture.Create();
            var messageId = MessageId.New();

            await fixture.Store.AddAsync(
                InboxIngestionFixture.NewMessage(messageId, "SecondHandler"));

            var results = new List<InboxWriteResult>();
            foreach (var consumer in FanOutConsumers)
            {
                results.Add(await fixture.Store.AddAsync(
                    InboxIngestionFixture.NewMessage(messageId, consumer)));
            }

            results.ShouldBe(
            [
                InboxWriteResult.Added,
                InboxWriteResult.AlreadyPresent,
                InboxWriteResult.Added,
            ]);

            (await fixture.CountAsync()).ShouldBe(3);
        });

    /// <summary>
    /// A genuine fault must still propagate. Absorbing every <c>DbUpdateException</c> would trade
    /// the spurious dead-letter for silent data loss, which is a worse defect — so this is the
    /// most important test in the file and it must run on every provider.
    /// </summary>
    /// <remarks>
    /// The failure mode is a NOT NULL violation, chosen because it is the one every provider
    /// enforces identically. A max-length overflow would be the obvious alternative and is the
    /// wrong choice: SQLite does not enforce length constraints, so the insert would succeed, no
    /// exception would be thrown, and this test would fail on the very provider the fast suite
    /// runs on — or, worse, be quietly restricted to PostgreSQL and stop guarding the fast path
    /// at all.
    /// </remarks>
    [Fact]
    public Task A_non_duplicate_persistence_failure_still_throws()
        => Task.Run(async () =>
        {
            await using var fixture = InboxIngestionFixture.Create();

            var message = InboxIngestionFixture.NewMessage(consumerType: "OrderPlacedHandler");
            message.Payload = null!;   // Payload is mapped IsRequired()

            await Should.ThrowAsync<DbUpdateException>(
                async () => await fixture.Store.AddAsync(message));

            // And the row must not be there — otherwise the post-hoc verification would have
            // reported AlreadyPresent and swallowed a real fault.
            (await fixture.CountAsync()).ShouldBe(0);
        });

    /// <summary>
    /// The dedup gate is the unique index, not a guard before the insert, so there is no
    /// time-of-check-to-time-of-use window. Losing the index is the one change that turns a
    /// deduplicating inbox into one that silently duplicates.
    /// </summary>
    [Fact]
    public Task The_unique_index_is_what_rejects_the_duplicate()
        => Task.Run(async () =>
        {
            await using var fixture = InboxIngestionFixture.Create();
            var message = InboxIngestionFixture.NewMessage(consumerType: "OrderPlacedHandler");

            await fixture.Store.AddAsync(message);

            // A different surrogate key, the same logical key: only the unique index can reject
            // this. If the primary key were still compound, this would be a PK violation instead
            // and the test would pass for the wrong reason.
            var duplicate = InboxIngestionFixture.Clone(message);
            duplicate.RowId.ShouldNotBe(message.RowId);

            (await fixture.Store.AddAsync(duplicate)).ShouldBe(InboxWriteResult.AlreadyPresent);
        });

    /// <summary>Fixture owning one isolated SQLite connection and its store.</summary>
    private sealed class InboxIngestionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private InboxIngestionFixture(SqliteConnection connection, TestMessagingDbContext context)
        {
            _connection = connection;
            Context = context;
            Store = new EfInboxStore<TestMessagingDbContext>(context, new FakeTimeProvider(Now));
        }

        public TestMessagingDbContext Context { get; }

        public EfInboxStore<TestMessagingDbContext> Store { get; }

        public static InboxIngestionFixture Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var context = new TestMessagingDbContext(
                new DbContextOptionsBuilder<TestMessagingDbContext>()
                    .UseSqlite(connection).Options);
            context.Database.EnsureCreated();

            return new InboxIngestionFixture(connection, context);
        }

        public static InboxMessage NewMessage(
            MessageId? messageId = null, string consumerType = "TestHandler")
            => new()
            {
                RowId = Guid.NewGuid(),
                MessageId = messageId ?? MessageId.New(),
                ConsumerType = consumerType,
                TenantId = "tenant-a",
                EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
                Payload = "{}",
                Status = InboxMessageStatus.Received,
                ReceivedAtUtc = Now,
            };

        /// <summary>Copies the logical key onto a fresh row, as a redelivery would.</summary>
        public static InboxMessage Clone(InboxMessage message)
            => NewMessage(message.MessageId, message.ConsumerType);

        public async Task<int> CountAsync()
        {
            await using var probe = new TestMessagingDbContext(
                new DbContextOptionsBuilder<TestMessagingDbContext>()
                    .UseSqlite(_connection).Options);

            return await probe.InboxMessages.AsNoTracking().CountAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
