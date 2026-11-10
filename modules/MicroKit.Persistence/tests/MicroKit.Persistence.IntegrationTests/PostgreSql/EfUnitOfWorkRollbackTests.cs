using MicroKit.Persistence.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.PostgreSql;

/// <summary>
/// #196 D5: the rollback that follows a failed operation never runs with a cancelled token and
/// never replaces the exception in flight — the caller always observes the original failure.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class EfUnitOfWorkRollbackTests(PostgreSqlSuiteFixture fixture)
{
    [DockerRequiredFact]
    public async Task ExecuteAsync_WhenTheTokenIsCancelledAndTheOperationThrows_SurfacesTheOriginalException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var transactions = new TransactionCountingInterceptor();
        await using var context = database.CreateContext(maxRetryCount: null, transactions);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        using var cts = new CancellationTokenSource();
        var failure = new InvalidOperationException("the operation failed after cancellation was requested");

        var thrown = await Should.ThrowAsync<Exception>(() => sut.ExecuteAsync(
            static async (s, ct) =>
            {
                s.Context.Rows.Add(new ExecutionRow { Id = Guid.NewGuid(), Name = "doomed" });
                await s.UnitOfWork.CommitAsync(ct);
                s.Cancellation.Cancel();
                throw s.Failure;
            },
            (UnitOfWork: sut, Context: context, Cancellation: cts, Failure: failure),
            cts.Token));

        thrown.ShouldBeSameAs(failure, "an OperationCanceledException from the rollback must not mask the original failure");
        transactions.RolledBack.ShouldBe(1, "the rollback still ran, with a token that was not cancelled");
        (await database.ReadRowsAsync()).ShouldBeEmpty("the flushed row was rolled back");
    }

    [DockerRequiredFact]
    public async Task ExecuteAsync_WhenTheRollbackItselfFails_SurfacesTheOriginalException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var failingRollback = new FailingRollbackInterceptor();
        await using var context = database.CreateContext(maxRetryCount: null, failingRollback);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var failure = new InvalidOperationException("the operation failed");

        var thrown = await Should.ThrowAsync<Exception>(() => sut.ExecuteAsync(
            static async (s, ct) =>
            {
                s.Context.Rows.Add(new ExecutionRow { Id = Guid.NewGuid(), Name = "doomed" });
                await s.UnitOfWork.CommitAsync(ct);
                throw s.Failure;
            },
            (UnitOfWork: sut, Context: context, Failure: failure)));

        failingRollback.Attempts.ShouldBe(1);
        thrown.ShouldBeSameAs(failure, "a failing rollback must not replace the exception in flight");
        (await database.ReadRowsAsync()).ShouldBeEmpty("the transaction was never committed");
    }
}
