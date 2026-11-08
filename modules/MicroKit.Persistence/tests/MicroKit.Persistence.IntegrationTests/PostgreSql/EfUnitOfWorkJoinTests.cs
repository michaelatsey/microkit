using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.PostgreSql;

/// <summary>
/// #196 D4: called while <c>Database.CurrentTransaction</c> is open, <c>ExecuteAsync</c> joins it.
/// It runs the operation directly — no execution strategy, no begin, commit or rollback — and
/// leaves the caller's transaction exactly as it found it, whether the operation succeeds or throws.
/// Every case runs with the default (non-retrying) strategy and with a retrying one.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class EfUnitOfWorkJoinTests(PostgreSqlFixture fixture)
{
    [DockerRequiredTheory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task ExecuteAsync_NestedInExecuteAsync_JoinsTheOuterTransaction(int? maxRetryCount)
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var transactions = new TransactionCountingInterceptor();
        await using var context = database.CreateContext(maxRetryCount, transactions);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        await sut.ExecuteAsync(static async (s, ct) =>
        {
            await s.Insert("outer", ct);
            var outer = s.Context.Database.CurrentTransaction.ShouldNotBeNull();

            await s.UnitOfWork.ExecuteAsync(static async (inner, token) =>
            {
                inner.Context.Database.CurrentTransaction.ShouldBeSameAs(inner.Outer);
                await inner.Insert("inner", token);
            }, s with { Outer = outer }, ct);

            s.Context.Database.CurrentTransaction.ShouldBeSameAs(outer);
            IsOpen(outer).ShouldBeTrue("the inner call must leave the outer transaction open");
        }, new JoinState(sut, context, null), CancellationToken.None);

        transactions.Begun.ShouldBe(1, "only the outer call begins a transaction");
        transactions.Committed.ShouldBe(1, "only the outer call commits");
        transactions.RolledBack.ShouldBe(0);
        (await database.ReadRowsAsync()).Select(r => r.Name).ShouldBe(["inner", "outer"]);
    }

    [DockerRequiredTheory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task ExecuteAsync_NestedInExecuteAsync_WhenTheInnerOperationThrows_NeitherCommitsNorRollsBack(int? maxRetryCount)
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var transactions = new TransactionCountingInterceptor();
        await using var context = database.CreateContext(maxRetryCount, transactions);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        await sut.ExecuteAsync(static async (s, ct) =>
        {
            await s.Insert("outer", ct);
            var outer = s.Context.Database.CurrentTransaction.ShouldNotBeNull();

            var failure = new InvalidOperationException("inner failed");
            var thrown = await Should.ThrowAsync<InvalidOperationException>(() =>
                s.UnitOfWork.ExecuteAsync(static async (inner, token) =>
                {
                    await inner.Insert("inner-before-failure", token);
                    throw inner.Failure!;
                }, s with { Failure = failure }, ct));

            thrown.ShouldBeSameAs(failure);
            s.Context.Database.CurrentTransaction.ShouldBeSameAs(outer);
            IsOpen(outer).ShouldBeTrue("a failing inner call must not end the caller's transaction");
        }, new JoinState(sut, context, null), CancellationToken.None);

        transactions.Committed.ShouldBe(1, "only the outer call commits");
        transactions.RolledBack.ShouldBe(0, "the inner call never rolls the caller's transaction back");
        (await database.ReadRowsAsync()).Select(r => r.Name).ShouldBe(["inner-before-failure", "outer"],
            "the row the inner operation flushed survives: nothing rolled it back");
    }

    [DockerRequiredTheory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task ExecuteAsync_InsideACallerTransaction_JoinsIt_AndLeavesItToTheCaller(int? maxRetryCount)
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var transactions = new TransactionCountingInterceptor();
        await using var context = database.CreateContext(maxRetryCount, transactions);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        // The documented way to open a transaction under any strategy: inside the strategy's own
        // ExecuteAsync (a retrying strategy rejects a user transaction opened outside it).
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var callerTransaction = await context.Database.BeginTransactionAsync();

            await sut.ExecuteAsync(static async (s, ct) =>
            {
                s.Context.Database.CurrentTransaction.ShouldBeSameAs(s.Outer);
                await s.Insert("joined", ct);
            }, new JoinState(sut, context, callerTransaction), CancellationToken.None);

            context.Database.CurrentTransaction.ShouldBeSameAs(callerTransaction);
            IsOpen(callerTransaction).ShouldBeTrue();
            transactions.Committed.ShouldBe(0, "the joined call never commits the caller's transaction");

            await callerTransaction.CommitAsync();
        });

        transactions.Begun.ShouldBe(1);
        transactions.Committed.ShouldBe(1);
        transactions.RolledBack.ShouldBe(0);
        (await database.ReadRowsAsync()).ShouldHaveSingleItem().Name.ShouldBe("joined");
    }

    [DockerRequiredTheory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task ExecuteAsync_InsideACallerTransaction_WhenTheOperationThrows_LeavesItOpenAndNotRolledBack(int? maxRetryCount)
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var transactions = new TransactionCountingInterceptor();
        await using var context = database.CreateContext(maxRetryCount, transactions);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var failure = new InvalidOperationException("joined operation failed");

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var callerTransaction = await context.Database.BeginTransactionAsync();

            var thrown = await Should.ThrowAsync<InvalidOperationException>(() =>
                sut.ExecuteAsync(static async (s, ct) =>
                {
                    await s.Insert("flushed-before-failure", ct);
                    throw s.Failure!;
                }, new JoinState(sut, context, callerTransaction) { Failure = failure }, CancellationToken.None));

            thrown.ShouldBeSameAs(failure);
            context.Database.CurrentTransaction.ShouldBeSameAs(callerTransaction);
            IsOpen(callerTransaction).ShouldBeTrue("the caller decides the fate of its own transaction");
            transactions.RolledBack.ShouldBe(0);

            await callerTransaction.CommitAsync();
        });

        transactions.Committed.ShouldBe(1);
        transactions.RolledBack.ShouldBe(0);
        (await database.ReadRowsAsync()).ShouldHaveSingleItem().Name.ShouldBe("flushed-before-failure",
            "the joined call rolled nothing back; the caller committed what was flushed");
    }

    [DockerRequiredTheory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task ExecuteAsync_NestedCommitFailure_IsTranslatedExactlyOnce(int? maxRetryCount)
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var existingId = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            seed.Rows.Add(new ExecutionRow { Id = existingId, Name = "existing" });
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext(maxRetryCount);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        var thrown = await Should.ThrowAsync<PersistenceException>(() =>
            sut.ExecuteAsync(static (s, ct) =>
                s.UnitOfWork.ExecuteAsync(static async (inner, token) =>
                {
                    inner.Context.Rows.Add(new ExecutionRow { Id = inner.DuplicateId, Name = "duplicate" });
                    await inner.UnitOfWork.CommitAsync(token);
                }, s, ct),
                new JoinState(sut, context, null) { DuplicateId = existingId },
                CancellationToken.None));

        thrown.InnerException.ShouldBeAssignableTo<DbUpdateException>(
            "one PersistenceException around the provider failure — never a PersistenceException in a PersistenceException");
    }

    // An IDbContextTransaction is open while its underlying DbTransaction still has a connection.
    private static bool IsOpen(IDbContextTransaction transaction) => transaction.GetDbTransaction().Connection is not null;

    private sealed record JoinState(
        EfUnitOfWork<ExecutionDbContext> UnitOfWork,
        ExecutionDbContext Context,
        IDbContextTransaction? Outer)
    {
        public Exception? Failure { get; init; }

        public Guid DuplicateId { get; init; }

        public async Task Insert(string name, CancellationToken ct)
        {
            Context.Rows.Add(new ExecutionRow { Id = Guid.NewGuid(), Name = name });
            await UnitOfWork.CommitAsync(ct);
        }
    }
}
