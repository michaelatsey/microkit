using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.PostgreSql;

/// <summary>
/// #196 D3: a transient failure of the command's <c>SaveChangesAsync</c> must be retried by the
/// execution strategy <c>ExecuteAsync</c> runs. The operation is driven the way
/// <c>TransactionBehavior</c> drives it: stage, <c>CommitAsync</c> inside <c>ExecuteAsync</c>,
/// <c>DiscardChanges</c> and rethrow on failure.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class EfUnitOfWorkRetryTests(PostgreSqlFixture fixture)
{
    [DockerRequiredFact]
    public async Task ExecuteAsync_WhenCommitFailsTransientlyOnce_RetriesTheOperationAndWritesOnce()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var failOnce = new FailingInsertInterceptor(failures: 1);
        await using var context = database.CreateContext(maxRetryCount: 3, failOnce);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, Guid.NewGuid());

        await sut.ExecuteAsync(StageAndCommit, state);

        failOnce.Thrown.ShouldBe(1);
        state.Attempts.ShouldBe(2, "the strategy must re-run the operation after a transient commit failure");
        (await database.ReadRowsAsync()).ShouldHaveSingleItem().Id.ShouldBe(state.RowId);
    }

    [DockerRequiredFact]
    public async Task ExecuteAsync_WhenRetriesAreExhausted_ThrowsPersistenceExceptionWrappingRetryLimitExceeded()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        await using var context = database.CreateContext(maxRetryCount: 2, new FailingInsertInterceptor(int.MaxValue));
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, Guid.NewGuid());

        var thrown = await Should.ThrowAsync<PersistenceException>(() => sut.ExecuteAsync(StageAndCommit, state));

        thrown.InnerException.ShouldBeOfType<RetryLimitExceededException>();
        state.Attempts.ShouldBe(3, "one attempt plus the two retries the strategy allows");
        (await database.ReadRowsAsync()).ShouldBeEmpty();
    }

    [DockerRequiredFact]
    public async Task ExecuteAsync_WhenCommitFailsNonTransiently_SurfacesTheOriginalPersistenceExceptionWithoutRetry()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var existingId = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            seed.Rows.Add(new ExecutionRow { Id = existingId, Name = "existing" });
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext(maxRetryCount: 3);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, existingId); // duplicate primary key: 23505

        var thrown = await Should.ThrowAsync<PersistenceException>(() => sut.ExecuteAsync(StageAndCommit, state));

        thrown.ShouldBeSameAs(state.LastCommitFailure, "the PersistenceException CommitAsync raised is the one the caller sees");
        thrown.InnerException.ShouldBeAssignableTo<DbUpdateException>();
        state.Attempts.ShouldBe(1, "a non-transient failure is never retried");
    }

    [DockerRequiredFact]
    public async Task ExecuteAsync_WithTheDefaultStrategy_WhenCommitFailsWithAProviderTransientError_ThrowsPersistenceException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        // NpgsqlException.IsTransient is true for an IOException cause: the provider's default,
        // non-retrying strategy recognises it and wraps it in an InvalidOperationException that
        // suggests enabling retries. The caller must still observe PersistenceException only.
        var transient = new FailingInsertInterceptor(
            failures: 1, () => new Npgsql.NpgsqlException("Simulated broken connection.", new IOException("reset")));
        await using var context = database.CreateContext(maxRetryCount: null, transient);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, Guid.NewGuid());

        var thrown = await Should.ThrowAsync<PersistenceException>(() => sut.ExecuteAsync(StageAndCommit, state));

        thrown.ShouldBeSameAs(state.LastCommitFailure);
        state.Attempts.ShouldBe(1, "the default strategy never retries");
    }

    [DockerRequiredFact]
    public async Task ExecuteAsync_WhenATransientFailureIsFollowedByANonTransientOne_SurfacesTheSecondPersistenceException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var existingId = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            seed.Rows.Add(new ExecutionRow { Id = existingId, Name = "existing" });
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext(maxRetryCount: 3, new FailingInsertInterceptor(failures: 1));
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, existingId); // attempt 1: transient; attempt 2: 23505

        var thrown = await Should.ThrowAsync<PersistenceException>(() => sut.ExecuteAsync(StageAndCommit, state));

        state.Attempts.ShouldBe(2);
        thrown.ShouldBeSameAs(state.LastCommitFailure, "the failure of the last attempt, not a stale one");
        var dbUpdate = thrown.InnerException.ShouldBeAssignableTo<DbUpdateException>();
        dbUpdate.ShouldNotBeNull();
        dbUpdate.InnerException.ShouldBeOfType<Npgsql.PostgresException>().SqlState.ShouldBe("23505");
    }

    [DockerRequiredFact]
    public async Task ExecuteAsync_WhenTheTransactionCommitFailsTransiently_RetriesTheOperation()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var failCommitOnce = new FailingCommitInterceptor(failures: 1, () => new TransientMarkerException());
        await using var context = database.CreateContext(maxRetryCount: 3, failCommitOnce);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);
        var state = new CommandState(sut, context, Guid.NewGuid());

        await sut.ExecuteAsync(StageAndCommit, state);

        failCommitOnce.Thrown.ShouldBe(1);
        state.Attempts.ShouldBe(2);
        (await database.ReadRowsAsync()).ShouldHaveSingleItem().Id.ShouldBe(state.RowId);
    }

    [DockerRequiredFact]
    public async Task ExecuteAsync_WithTheDefaultStrategy_WhenTheTransactionCommitFailsTransiently_ThrowsPersistenceException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var failCommit = new FailingCommitInterceptor(
            failures: 1, () => new Npgsql.NpgsqlException("Simulated broken connection.", new IOException("reset")));
        await using var context = database.CreateContext(maxRetryCount: null, failCommit);
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        var thrown = await Should.ThrowAsync<PersistenceException>(() =>
            sut.ExecuteAsync(StageAndCommit, new CommandState(sut, context, Guid.NewGuid())));

        thrown.InnerException.ShouldNotBeNull();
    }

    [DockerRequiredFact]
    public async Task ExecuteAsync_WhenADeferredConstraintFailsAtCommit_ThrowsPersistenceException()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        await using (var ddl = database.CreateContext())
        {
            await ddl.Database.ExecuteSqlRawAsync(
                "CREATE TABLE deferred_codes (code text NOT NULL, CONSTRAINT uq_deferred_code UNIQUE (code) DEFERRABLE INITIALLY DEFERRED)");
        }

        await using var context = database.CreateContext();
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        // Both inserts succeed; the duplicate is only detected by the transaction's COMMIT.
        var thrown = await Should.ThrowAsync<PersistenceException>(() => sut.ExecuteAsync(
            static async (ctx, ct) => await ctx.Database.ExecuteSqlRawAsync(
                "INSERT INTO deferred_codes (code) VALUES ('A'), ('A')", ct),
            context));

        thrown.InnerException.ShouldBeOfType<Npgsql.PostgresException>().SqlState.ShouldBe("23505");
    }

    [DockerRequiredFact]
    public async Task ExecuteAsync_WhenTheTransactionCommitFailsAfterSaveChanges_RetriesFromACleanChangeTracker()
    {
        await using var database = await ExecutionDatabase.CreateAsync(fixture);
        var counterId = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            seed.Rows.Add(new ExecutionRow { Id = counterId, Name = "0" });
            await seed.SaveChangesAsync();
        }

        // SaveChanges succeeds and accepts the change; only the transaction's COMMIT fails. The
        // operation does not discard: a plain ITransactionalContext caller has no reason to.
        await using var context = database.CreateContext(maxRetryCount: 3,
            new FailingCommitInterceptor(failures: 1, () => new TransientMarkerException()));
        var sut = new EfUnitOfWork<ExecutionDbContext>(context);

        await sut.ExecuteAsync(static async (s, ct) =>
        {
            var counter = await s.Context.Rows.SingleAsync(r => r.Id == s.Id, ct);
            counter.Name = (int.Parse(counter.Name, System.Globalization.CultureInfo.InvariantCulture) + 1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            await s.UnitOfWork.CommitAsync(ct);
        }, (UnitOfWork: sut, Context: context, Id: counterId));

        (await database.ReadRowsAsync()).ShouldHaveSingleItem().Name.ShouldBe("1",
            "the retried attempt must re-read the rolled-back row, not the first attempt's tracked instance");
    }

    private static async Task StageAndCommit(CommandState state, CancellationToken ct)
    {
        state.Attempts++;
        try
        {
            state.Context.Rows.Add(new ExecutionRow { Id = state.RowId, Name = "row" });
            await state.UnitOfWork.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            if (ex is PersistenceException persistence)
                state.LastCommitFailure = persistence;

            state.UnitOfWork.DiscardChanges(); // TransactionBehavior's non-commit exit
            throw;
        }
    }

    private sealed class CommandState(EfUnitOfWork<ExecutionDbContext> unitOfWork, ExecutionDbContext context, Guid rowId)
    {
        public EfUnitOfWork<ExecutionDbContext> UnitOfWork { get; } = unitOfWork;

        public ExecutionDbContext Context { get; } = context;

        public Guid RowId { get; } = rowId;

        public int Attempts { get; set; }

        public PersistenceException? LastCommitFailure { get; set; }
    }
}
