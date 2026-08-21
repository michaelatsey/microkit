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
    /// Optional extra messaging registration (e.g. <c>AddMessageHandler</c>), applied after the
    /// transports are wired.
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

        // Registration ORDER is load-bearing twice over:
        //   - AddMediatRTransport() throws InvalidOperationException if no IOutboxDispatcher is
        //     registered yet, so AddInProcessTransport() must come first;
        //   - AddMediatRTransport() registers IDomainEventsDispatcher with a plain AddScoped that
        //     must win over the core's TryAdd from AddMicroKitMediatR above.
        // AddHostedService<OutboxWorker> is registered by AddMicroKitMessaging and starts nothing
        // without an IHost. It is left alone; every drain goes through DrainOnceAsync.
        var messaging = services.AddMicroKitMessaging()
            .AddEfCoreOutbox<E2EDbContext>()
            .AddInProcessTransport()
            .AddMediatRTransport();

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
}
