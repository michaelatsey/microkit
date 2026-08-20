using MediatR;
using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.MediatR.IntegrationTests.Fixtures;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Result;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.IntegrationTests.Pipeline;

/// <summary>
/// The integration proof for ADR-MEDIATR-012 and ADR-005: <c>TransactionBehavior</c> calling
/// <see cref="MicroKit.Persistence.Abstractions.IUnitOfWork.DiscardChanges"/> actually keeps a
/// failed command's row out of the database.
/// </summary>
/// <remarks>
/// <para>
/// The unit suite (<c>TransactionBehaviorTests</c>) proves the <em>call</em> against an NSubstitute
/// double, and <c>EfUnitOfWorkDiscardChangesTests</c> proves <c>ChangeTracker.Clear()</c> in
/// isolation. Neither proves the <em>chain</em>. These tests do, against a real EF Core
/// <c>DbContext</c> on SQLite, through the real <c>AddMicroKitPersistence</c> /
/// <c>AddUnitOfWork&lt;TContext&gt;()</c> / <c>AddTransactionBehavior()</c> registrations.
/// </para>
/// <para>
/// <b>One scope for both commands is the whole point.</b> <c>DbContext</c> is scoped, not
/// per-command. The defect is only reachable where a scope outlives a single command — a batch
/// loop, a scheduled job, a Blazor Server circuit, or a test like this one. The second command's
/// flush is precisely what would carry the first command's residue to the database.
/// </para>
/// <para>
/// No MicroKit.Messaging: the scenario raises no events, so the core scoped
/// <c>DomainEventDispatcher</c> — which needs only <c>IDomainEventsProvider</c>, supplied by
/// <c>AddUnitOfWork&lt;TContext&gt;()</c> — runs unmodified with nothing stubbed.
/// </para>
/// </remarks>
public sealed class TransactionBehaviorPersistenceTests
{
    private const string RejectedRow = "from-rejected-command";
    private const string ThrownRow = "from-thrown-command";
    private const string SucceededRow = "from-successful-command";

    // Non-commit exit 1 (ADR-MEDIATR-012). Nothing throws, so the database transaction commits
    // clean and nothing else will ever clear what the handler staged.
    [Fact]
    public async Task Handle_WhenAFailedCommandPrecedesASuccessfulOneInTheSameScope_TheFailedCommandsRowIsNotPersisted()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateSchemaAsync(connection);
        await using var provider = BuildProvider(connection);

        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var rejected = await mediator.SendCommandAsync<RejectAggregateCommand, Result<string>>(
                new RejectAggregateCommand(RejectedRow));
            rejected.IsFailure.ShouldBeTrue("the first command must fail its business rule");

            // Same scope, same DbContext: this flush is what would write the rejected row too.
            var succeeded = await mediator.SendCommandAsync<CreateAggregateCommand, Result<string>>(
                new CreateAggregateCommand(SucceededRow));
            succeeded.IsSuccess.ShouldBeTrue();
        }

        var names = await ReadAllNamesAsync(connection);
        names.ShouldBe([SucceededRow],
            "a command that failed its business rule must not persist its data anyway");
    }

    // Non-commit exit 2 (ADR-005 §2). The rollback undoes what was WRITTEN; it does not reset the
    // change tracker. A fix scoped to Result.IsFailure would leave this path open — and for the
    // persistence layer the thrown path is the more common failure mode.
    [Fact]
    public async Task Handle_WhenAThrowingCommandPrecedesASuccessfulOneInTheSameScope_TheThrownCommandsRowIsNotPersisted()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateSchemaAsync(connection);
        await using var provider = BuildProvider(connection);

        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var thrown = await Should.ThrowAsync<InvalidOperationException>(
                async () => await mediator.SendCommandAsync<ThrowAfterStagingCommand, Result<string>>(
                    new ThrowAfterStagingCommand(ThrownRow)));
            thrown.Message.ShouldBe("handler failed after staging",
                "the behavior rethrows bare — the original exception must reach the caller intact");

            var succeeded = await mediator.SendCommandAsync<CreateAggregateCommand, Result<string>>(
                new CreateAggregateCommand(SucceededRow));
            succeeded.IsSuccess.ShouldBeTrue();
        }

        var names = await ReadAllNamesAsync(connection);
        names.ShouldBe([SucceededRow],
            "staged entities survive a rollback exactly as they survive a business failure");
    }

    // Positive control. Without it, both tests above would pass against a harness that silently
    // writes nothing at all — proving nothing.
    [Fact]
    public async Task Handle_WhenASingleCommandSucceeds_ItsRowIsPersisted()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateSchemaAsync(connection);
        await using var provider = BuildProvider(connection);

        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var succeeded = await mediator.SendCommandAsync<CreateAggregateCommand, Result<string>>(
                new CreateAggregateCommand(SucceededRow));
            succeeded.IsSuccess.ShouldBeTrue();
        }

        var names = await ReadAllNamesAsync(connection);
        names.ShouldBe([SucceededRow],
            "the harness must actually write, or the absence assertions above prove nothing");
    }

    // A SQLite in-memory database lives exactly as long as its connection — the test keeps it open
    // for the duration. Precedent: MicroKit.Persistence.IntegrationTests/EfUnitOfWorkDiscardChangesTests.
    private const string InMemoryConnectionString = "DataSource=:memory:";

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(InMemoryConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    // Built OUTSIDE the container, on the same open connection: schema creation and verification
    // must never see the scoped context's tracked state.
    private static TransactionTestDbContext NewContext(SqliteConnection connection)
        => new(new DbContextOptionsBuilder<TransactionTestDbContext>().UseSqlite(connection).Options);

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        await using var context = NewContext(connection);
        await context.Database.EnsureCreatedAsync();
    }

    private static async Task<List<string>> ReadAllNamesAsync(SqliteConnection connection)
    {
        await using var context = NewContext(connection);
        return await context.Aggregates.AsNoTracking().Select(a => a.Name).ToListAsync();
    }

    private static ServiceProvider BuildProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITransactionTestWriter, EfTransactionTestWriter>();

        // The real chain — nothing under test is substituted. AddUnitOfWork binds one scoped
        // EfUnitOfWork<TContext> to IUnitOfWork and ITransactionalContext, so the flush and the
        // discard both target the DbContext the transaction was opened on.
        services.AddMicroKitPersistence(p => p
            .AddEntityFrameworkCore()
            .AddDbContext<TransactionTestDbContext>(o => o.UseSqlite(connection))
            .AddUnitOfWork<TransactionTestDbContext>());

        // LoggingBehavior is deliberately omitted: the subject is order 700, and AddDbContext
        // already calls AddLogging(). Layering an ILogger<> -> NullLogger<> open generic on top of
        // that would make the winner depend on registration order, for zero coverage.
        services.AddMicroKitMediatR(cfg => cfg
            .FromAssemblyContaining<CreateAggregateCommand>()
            .AddTransactionBehavior());

        // ValidateScopes, but NOT ValidateOnBuild: everything under test is scoped, so scope
        // validation is a real guard — whereas ValidateOnBuild would eagerly validate every handler
        // in the assembly, including those needing AttemptCounter and DomainEventLog, which this
        // container deliberately does not register.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
