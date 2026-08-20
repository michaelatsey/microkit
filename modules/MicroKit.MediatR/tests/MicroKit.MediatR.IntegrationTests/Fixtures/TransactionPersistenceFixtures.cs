using MicroKit.Result;
using Microsoft.EntityFrameworkCore;
using static MicroKit.Result.Result;

namespace MicroKit.MediatR.IntegrationTests.Fixtures;

// Fixtures for TransactionBehaviorPersistenceTests — the only tests in this project backed by a
// real database. Everything here exists to make ONE question answerable: does a failed command's
// staged row reach the database when a later command in the SAME DI scope flushes?

/// <summary>
/// A plain persisted entity. It deliberately does NOT implement <c>IHasDomainEvents</c>, so
/// <c>EfDomainEventsProvider.DrainDomainEvents()</c> skips it and the scenario stays event-free —
/// no outbox, no MicroKit.Messaging, nothing between the behavior and the change tracker.
/// </summary>
internal sealed class TransactionTestAggregate
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Identifies which command staged the row, so assertions can name it.</summary>
    public string Name { get; set; } = string.Empty;
}

internal sealed class TransactionTestDbContext(DbContextOptions<TransactionTestDbContext> options)
    : DbContext(options)
{
    public DbSet<TransactionTestAggregate> Aggregates => Set<TransactionTestAggregate>();
}

/// <summary>
/// The handlers' staging port. A <c>DbContext</c> constructor parameter in a handler is flagged by
/// <c>.claude/rules/no-handler-coupling.md</c>, and staging is all these handlers need — the flush
/// and the discard belong to <c>TransactionBehavior</c>, which is the subject under test.
/// </summary>
internal interface ITransactionTestWriter
{
    void Stage(string name);
}

internal sealed class EfTransactionTestWriter(TransactionTestDbContext context) : ITransactionTestWriter
{
    // Stage only. Never SaveChangesAsync, never DiscardChanges — both exits are the behavior's.
    public void Stage(string name) => context.Add(new TransactionTestAggregate { Name = name });
}

// Error is abstract — a concrete subtype is required to construct Result.Failure.
internal sealed record StagedWorkRejectedError()
    : Error(ErrorCode.From("TEST.REJECTED"), "the command staged work and then rejected it");

// ── Non-commit exit 1 — business failure ───────────────────────────────────

internal sealed record RejectAggregateCommand(string Name) : ICommand<Result<string>>;

internal sealed class RejectAggregateHandler(ITransactionTestWriter writer)
    : ICommandHandler<RejectAggregateCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(RejectAggregateCommand command, CancellationToken ct = default)
    {
        writer.Stage(command.Name);
        return new(Failure<string>(new StagedWorkRejectedError()));
    }
}

// ── Non-commit exit 2 — thrown exception ───────────────────────────────────

internal sealed record ThrowAfterStagingCommand(string Name) : ICommand<Result<string>>;

internal sealed class ThrowAfterStagingHandler(ITransactionTestWriter writer)
    : ICommandHandler<ThrowAfterStagingCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(ThrowAfterStagingCommand command, CancellationToken ct = default)
    {
        writer.Stage(command.Name);
        throw new InvalidOperationException("handler failed after staging");
    }
}

// ── The commit exit — and the second command in the shared scope, whose flush
//    is exactly what would carry the failed command's residue to the database ─

internal sealed record CreateAggregateCommand(string Name) : ICommand<Result<string>>;

internal sealed class CreateAggregateHandler(ITransactionTestWriter writer)
    : ICommandHandler<CreateAggregateCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(CreateAggregateCommand command, CancellationToken ct = default)
    {
        writer.Stage(command.Name);
        return new(Success(command.Name));
    }
}
