using Microsoft.Extensions.Logging;
using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.Messaging.MediatR.DependencyInjection;
using MicroKit.Persistence.EntityFrameworkCore;

namespace MicroKit.Messaging.MediatR.IntegrationTests.Infrastructure;

/// <summary>
/// Shared wiring for the end-to-end suite: the connection, the schema, the container, the drain
/// seam, and the verification reads.
/// </summary>
internal static class E2EHarness
{
    // A SQLite in-memory database lives exactly as long as its connection — each test keeps it open
    // for its whole duration. Precedent: TransactionBehaviorPersistenceTests.
    private const string InMemoryConnectionString = "DataSource=:memory:";

    internal static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(InMemoryConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    // Built OUTSIDE the container, on the SAME open connection: schema creation and verification
    // must never see a scoped context's tracked state.
    internal static E2EDbContext NewContext(SqliteConnection connection)
        => new(new DbContextOptionsBuilder<E2EDbContext>().UseSqlite(connection).Options);

    internal static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        await using var context = NewContext(connection);
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>Reads every outbox row through a fresh verification context.</summary>
    internal static async Task<List<OutboxMessage>> ReadOutboxAsync(SqliteConnection connection)
    {
        await using var context = NewContext(connection);
        return await context.OutboxMessages.AsNoTracking().ToListAsync();
    }

    /// <summary>Reads every inbox row through a fresh verification context.</summary>
    internal static async Task<List<InboxMessage>> ReadInboxAsync(SqliteConnection connection)
    {
        await using var context = NewContext(connection);
        return await context.InboxMessages.AsNoTracking().ToListAsync();
    }

    /// <summary>
    /// Builds the real container. Nothing under test is substituted.
    /// </summary>
    /// <param name="connection">
    /// The open connection every context must be built on — passed as an INSTANCE, never as a
    /// connection string. <c>PassThroughExecutionScopeFactory</c> creates the per-message scope from
    /// the ROOT <see cref="IServiceScopeFactory"/>, so the outbox processor resolves a different
    /// <see cref="E2EDbContext"/> instance than the command used. A connection string would give
    /// that second context its own empty in-memory database and the drain would find no rows —
    /// a failure with nothing to do with the behaviour under test.
    /// </param>
    /// <param name="recorder">The invocation recorder, registered as a singleton.</param>
    /// <param name="logs">Captures every log record so the drain's decisions are observable.</param>
    /// <param name="configureMessaging">
    /// Optional extra messaging registration, applied after the messaging chain is wired — a
    /// transport, the integration-event entry points, message handlers.
    /// </param>
    internal static ServiceProvider BuildProvider(
        SqliteConnection connection,
        HandlerInvocationRecorder recorder,
        CapturingLoggerProvider logs,
        Action<MessagingBuilder>? configureMessaging = null)
    {
        var services = new ServiceCollection();

        // A real host always has logging, and the drain genuinely needs it: six types registered by
        // AddMicroKitMessaging and the transports take an ILogger<T>, so without this the
        // coordinator cannot even be activated (see L0-FINDINGS.md, Finding #1).
        // AddLogging() alone would still discard every record — the capturing provider is what makes
        // the processor's retry and dead-letter decisions observable.
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);   // capture everything; tests filter on read
            b.AddProvider(logs);
        });
        services.AddSingleton(logs);

        // Singleton: handlers run in the processor's per-message scope, not the command's.
        services.AddSingleton(recorder);
        services.AddScoped<IE2EWriter, EfE2EWriter>();

        // AddUnitOfWork binds one scoped EfUnitOfWork<E2EDbContext> to IUnitOfWork,
        // ITransactionalContext and ITransactionalUnitOfWork, and registers the
        // EfDomainEventsProvider that P1 drains — so dispatch and flush target the same DbContext
        // the transaction was opened on.
        services.AddMicroKitPersistence(p => p
            .AddEntityFrameworkCore()
            .AddDbContext<E2EDbContext>(o => o.UseSqlite(connection))
            .AddUnitOfWork<E2EDbContext>());

        // TransactionBehavior (order 700) is the dispatch+commit owner: it calls
        // DispatchEventsAsync and only then CommitAsync, so the outbox rows staged by P4 are
        // flushed in the same SaveChangesAsync as the aggregate (ADR-MSG-012).
        services.AddMicroKitMediatR(cfg => cfg
            .FromAssemblyContaining<CreateWidgetCommand>()
            .AddTransactionBehavior());

        // NO TRANSPORT IS REGISTERED HERE, and that is the point of this composition rather than an
        // omission. Every suite on this harness exercises the domain-event path, which stages
        // MessageKind.Notification rows and fans them out in process through MediatR — so the
        // notification-only host of ADR-MSG-019 is exactly what these tests need, and this harness
        // is the standing proof that it composes and drains. MediatROutboxDispatcher's inner is
        // null throughout; a MessageKind.Contract row would raise OutboxConfigurationException, and
        // nothing here stages one.
        //
        // Registration order is no longer load-bearing anywhere. The standard dispatcher lives in a
        // keyed slot only MicroKit.Messaging writes, so AddMediatRDomainEvents() never competes for
        // the seam and no longer requires a transport to have been registered first; the glue
        // contributes an IDomainEventsSink to the single core dispatcher rather than registering a
        // rival one (ADR-MEDIATR-014).
        //
        // AddHostedService<OutboxWorker> is registered by AddMicroKitMessaging and starts nothing
        // without an IHost. It is left alone; every drain goes through DrainOnceAsync or
        // DrainInboxOnceAsync. The same is true of IntegrationEventRegistryValidator — no host, so
        // no startup check runs, which is why a suite may compose freely and assert on the drain.
        var messaging = services.AddMicroKitMessaging()
            .AddEfCoreOutbox<E2EDbContext>()
            .AddMediatRDomainEvents();

        configureMessaging?.Invoke(messaging);

        // ValidateScopes, but NOT ValidateOnBuild: everything under test is scoped, so scope
        // validation is a real guard — whereas ValidateOnBuild would eagerly validate every handler
        // in the scanned assembly, including fixtures belonging to the other scenarios that this
        // particular container deliberately does not wire.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>
    /// Runs exactly one outbox processing cycle. The only drain seam in this suite.
    /// </summary>
    /// <remarks>
    /// The return value of <c>ExecuteAsync</c> is DISCARDED on purpose — never captured, never
    /// asserted on. It is <see cref="Task"/> today and becomes <c>ValueTask&lt;OutboxBatchResult&gt;</c>
    /// in a pending rewrite; a bare <c>await</c> compiles against both, whereas capturing it would
    /// couple this harness to a signature that is about to change.
    /// </remarks>
    internal static async Task DrainOnceAsync(IServiceProvider root, CancellationToken ct)
    {
        await using var scope = root.CreateAsyncScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IOutboxCoordinator>();
        await coordinator.ExecuteAsync(ct);
    }

    /// <summary>
    /// Runs exactly one inbox processing cycle — the receiving half of the round trip.
    /// </summary>
    /// <remarks>
    /// Rows reach the inbox through <see cref="IEnvelopeReceiver"/>, never from the producing side:
    /// a test hands it an envelope taken from <c>RecordingMessageTransport.Sent</c>, which is what
    /// makes the two halves genuinely separate rather than a fan-out wearing a wire's clothes.
    /// The return value is discarded for the same reason as above.
    /// </remarks>
    internal static async Task DrainInboxOnceAsync(IServiceProvider root, CancellationToken ct)
    {
        await using var scope = root.CreateAsyncScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IInboxCoordinator>();
        await coordinator.ExecuteAsync(ct);
    }
}
