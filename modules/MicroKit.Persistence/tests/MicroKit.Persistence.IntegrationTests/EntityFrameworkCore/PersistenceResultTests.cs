using System.Transactions;
using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Identifiers;
using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.EntityFrameworkCore;
using MicroKit.Persistence.EntityFrameworkCore.PostgreSql;
using MicroKit.Persistence.EntityFrameworkCore.SqlServer;
using MicroKit.Persistence.IntegrationTests.PostgreSql;
using MicroKit.Persistence.IntegrationTests.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.EntityFrameworkCore;

using MicroKit.Result;

/// <summary>
/// #197 phase 3: the <see cref="IResultUnitOfWork"/> path of <see cref="EfUnitOfWork{TContext}"/> returns
/// unique, foreign-key and concurrency failures as <c>PERSISTENCE.&lt;ROOT&gt;.&lt;REASON&gt;</c> conflicts,
/// converted only once the transaction it owns is rolled back. Every scenario runs on PostgreSQL and on
/// SQL Server.
/// </summary>
public abstract class PersistenceResultScenarios
{
    private static readonly Error BusinessFailure =
        Error.BusinessRule(ErrorCode.From("ACCOUNT.CLOSED"), "The account is closed.");

    /// <summary>Configures the provider on <paramref name="database"/>, with a retrying strategy when asked.</summary>
    protected abstract void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null);

    /// <summary>The provider's classifier.</summary>
    protected abstract IDatabaseFailureClassifier Classifier { get; }

    /// <summary>Registers the context the way an application does, through the provider package.</summary>
    protected abstract void Register(EfCoreBuilder builder, string database);

    /// <summary>
    /// Whether a foreign-key violation on a table whose entity declares a check constraint is converted:
    /// PostgreSQL tells the two apart by SQLSTATE; SQL Server reports both as 547, so it is not.
    /// </summary>
    protected abstract bool ForeignKeyOnACheckedTableIsConverted { get; }

    // ---- What is converted ----------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task TryExecuteAsync_WhenTheSecondFlushViolatesAUniqueIndex_ReturnsDuplicate_AndPersistsNeitherRow()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                await s.Accounts.AddAsync(NewAccount("ada@example.com"), ct);
                await s.UnitOfWork.CommitAsync(ct);
                await s.Accounts.AddAsync(NewAccount("ada@example.com"), ct);
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork));

        result.Error.ShouldBe(PersistenceErrors.Duplicate("ACCOUNT"));
        result.Error.Code.Value.ShouldBe("PERSISTENCE.ACCOUNT.DUPLICATE");
        result.Error.Category.ShouldBe(ErrorCategory.Conflict);
        result.Error.Message.ShouldNotContain("account", Case.Insensitive, "no table name");
        result.Error.Message.ShouldNotContain("email", Case.Insensitive, "no column name");
        result.Error.Message.ShouldNotContain("IX_", Case.Sensitive, "no constraint name");
        await using var verify = db.Create();
        (await verify.Set<Account>().CountAsync()).ShouldBe(0, "the first flush is rolled back with the transaction");
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_WhenADeletedRootIsStillReferenced_ReturnsReference()
    {
        await using var db = await CreateDatabaseAsync();
        var (accountId, _) = await SeedAccountWithConsignmentAsync(db);
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                var account = await s.Accounts.FindAsync(s.Id, ct);
                await s.Accounts.DeleteAsync(account!, ct);
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork, Id: accountId));

        result.Error.ShouldBe(PersistenceErrors.Reference("ACCOUNT"));
        await using var verify = db.Create();
        (await verify.Set<Account>().CountAsync()).ShouldBe(1);
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_WhenAForeignKeyIsViolatedOnATableWithACheckConstraint_ConvertsPerProvider()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        Task<Result> AddOrphan() => unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                await s.Consignments.AddAsync(NewConsignment(new AccountId(Guid.NewGuid()), "ORPHAN"), ct);
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Consignments: Consignments(context), UnitOfWork: unitOfWork)).AsTask();

        if (ForeignKeyOnACheckedTableIsConverted)
        {
            (await AddOrphan()).Error.ShouldBe(PersistenceErrors.Reference("DELIVERY"));
        }
        else
        {
            // SQL Server reports the foreign-key violation as 547, like a check violation, and Consignment
            // declares a check constraint: the model cannot tell them apart, so it is not converted.
            var thrown = await Should.ThrowAsync<PersistenceException>(AddOrphan);
            thrown.InnerException.ShouldBeAssignableTo<DbUpdateException>();
        }
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_WhenADeclaredCheckConstraintIsViolated_PropagatesUnconverted()
    {
        await using var db = await CreateDatabaseAsync();
        var (accountId, _) = await SeedAccountWithConsignmentAsync(db);
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var thrown = await Should.ThrowAsync<PersistenceException>(async () => await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                await s.Consignments.AddAsync(NewConsignment(s.AccountId, "HEAVY", weight: -1), ct);
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Consignments: Consignments(context), UnitOfWork: unitOfWork, AccountId: accountId)));

        thrown.InnerException.ShouldBeAssignableTo<DbUpdateException>();
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_WhenAnotherContextChangedTheAggregate_ReturnsConcurrency()
    {
        await using var db = await CreateDatabaseAsync();
        var (accountId, _) = await SeedAccountWithConsignmentAsync(db);
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                var account = await s.Accounts.FindAsync(s.Id, ct);
                account!.Notes.Add(new AccountNote { Text = "mine" });

                await using (var other = s.Db.Create())
                {
                    var theirs = await Accounts(other).FindAsync(s.Id, ct);
                    theirs!.Email = "theirs@example.com";
                    await new EfUnitOfWork<ResultsDbContext>(other).CommitAsync(ct);
                }

                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork, Id: accountId, Db: db));

        result.Error.ShouldBe(PersistenceErrors.Concurrency("ACCOUNT"));
        await using var verify = db.Create();
        (await verify.Set<AccountNote>().CountAsync(n => n.Text == "mine")).ShouldBe(0, "nothing of the losing save is written");
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_WithAStaleExpectedVersion_ReturnsConcurrency()
    {
        await using var db = await CreateDatabaseAsync();
        var (accountId, _) = await SeedAccountWithConsignmentAsync(db);
        await using (var other = db.Create())
        {
            (await Accounts(other).FindAsync(accountId))!.Email = "moved@example.com";
            await new EfUnitOfWork<ResultsDbContext>(other).CommitAsync();
        }

        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                var account = await s.Accounts.FindAsync(s.Id, expectedVersion: 1, ct);
                account!.Email = "stale@example.com";
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success(account.Email);
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork, Id: accountId));

        result.Error.ShouldBe(PersistenceErrors.Concurrency("ACCOUNT"));
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_WhenAnAddedRootIsFoundWithAVersionOtherThanZero_ReturnsConcurrency()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                await s.Accounts.AddAsync(NewAccount("new@example.com"), ct);
                await s.UnitOfWork.CommitAsync(ct);
                var staged = NewAccount("staged@example.com");
                await s.Accounts.AddAsync(staged, ct);
                await s.Accounts.FindAsync(staged.Id, expectedVersion: 3, ct); // not inserted yet: version 0
                return Result.Success();
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork));

        result.Error.ShouldBe(PersistenceErrors.Concurrency("ACCOUNT"));
        await using var verify = db.Create();
        (await verify.Set<Account>().CountAsync()).ShouldBe(0, "the earlier flush is rolled back");
    }

    // ---- Which root the code names --------------------------------------------------------------

    [DockerRequiredFact]
    public async Task TheCode_NamesTheRootOfAFailingChild()
    {
        await using var db = await CreateDatabaseAsync();
        var (accountId, _) = await SeedAccountWithConsignmentAsync(db);
        var conflict = new ConflictingSaveInterceptor(static e => e.Entity is AccountNote);
        await using var context = db.Create(o => o.AddInterceptors(conflict));
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                (await s.Accounts.FindAsync(s.Id, ct))!.Notes.Add(new AccountNote { Text = "child" });
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork, Id: accountId));

        result.Error.Code.Value.ShouldBe("PERSISTENCE.ACCOUNT.CONCURRENCY");
    }

    [DockerRequiredFact]
    public async Task TheCode_HasNoRoot_WhenTheFailingEntriesBelongToTwoAggregates()
    {
        await using var db = await CreateDatabaseAsync();
        var (accountId, shipmentId) = await SeedAccountWithConsignmentAsync(db);
        var conflict = new ConflictingSaveInterceptor(static e => e.Entity is Account or Consignment);
        await using var context = db.Create(o => o.AddInterceptors(conflict));
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                (await s.Accounts.FindAsync(s.AccountId, ct))!.Email = "both@example.com";
                (await s.Consignments.FindAsync(s.ConsignmentId, ct))!.Weight = 7;
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Accounts: Accounts(context), Consignments: Consignments(context), UnitOfWork: unitOfWork, AccountId: accountId, ConsignmentId: shipmentId));

        result.Error.Code.Value.ShouldBe("PERSISTENCE.CONCURRENCY");
    }

    [DockerRequiredFact]
    public async Task TheCode_HasNoRoot_WhenTheFailingEntityIsOutsideEveryAggregate()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                s.Context.Add(new AuditEntry { Id = Guid.NewGuid(), Text = "once" });
                await s.UnitOfWork.CommitAsync(ct);
                s.Context.Add(new AuditEntry { Id = Guid.NewGuid(), Text = "once" });
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Context: context, UnitOfWork: unitOfWork));

        result.Error.Code.Value.ShouldBe("PERSISTENCE.DUPLICATE");
    }

    [DockerRequiredFact]
    public async Task TheCode_HasNoRoot_WhenOneFailingEntryIsOutsideEveryAggregate()
    {
        await using var db = await CreateDatabaseAsync();
        var (accountId, _) = await SeedAccountWithConsignmentAsync(db);
        var conflict = new ConflictingSaveInterceptor(static e => e.Entity is Account or AuditEntry);
        await using var context = db.Create(o => o.AddInterceptors(conflict));
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                (await s.Accounts.FindAsync(s.Id, ct))!.Email = "mixed@example.com";
                s.Context.Add(new AuditEntry { Id = Guid.NewGuid(), Text = "outside" });
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Accounts: Accounts(context), Context: context, UnitOfWork: unitOfWork, Id: accountId));

        result.Error.Code.Value.ShouldBe("PERSISTENCE.CONCURRENCY", "a root is never guessed");
    }

    [DockerRequiredFact]
    public async Task TheCode_UsesTheNameTheModelFixes()
    {
        await using var db = await CreateDatabaseAsync();
        var (accountId, _) = await SeedAccountWithConsignmentAsync(db);
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                await s.Consignments.AddAsync(NewConsignment(s.AccountId, "SEEDED"), ct); // the seeded reference
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Consignments: Consignments(context), UnitOfWork: unitOfWork, AccountId: accountId));

        result.Error.Code.Value.ShouldBe("PERSISTENCE.DELIVERY.DUPLICATE");
        context.Model.FindEntityType(typeof(Account))!.FindAnnotation("MicroKit:ErrorName")!.Value.ShouldBe("ACCOUNT");
    }

    // ---- A failed Result the operation returns ------------------------------------------------

    [DockerRequiredFact]
    public async Task TryExecuteAsync_WhenTheOperationReturnsAFailure_ReturnsIt_AndPersistsNothing()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var counter = new TransactionCountingInterceptor();
        await using var counted = db.Create(o => o.AddInterceptors(counter));
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(counted);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                await s.Accounts.AddAsync(NewAccount("flushed@example.com"), ct);
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Failure(BusinessFailure);
            },
            (Accounts: Accounts(counted), UnitOfWork: unitOfWork));

        result.Error.ShouldBeSameAs(BusinessFailure);
        counter.Committed.ShouldBe(0);
        counter.RolledBack.ShouldBe(1);
        (await context.Set<Account>().CountAsync()).ShouldBe(0, "a failed command persists none of its work");
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_OfAValue_WhenTheOperationReturnsAFailure_ReturnsIt_AndPersistsNothing()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                var account = NewAccount("flushed@example.com");
                await s.Accounts.AddAsync(account, ct);
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Failure<AccountId>(BusinessFailure);
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork));

        result.Error.ShouldBeSameAs(BusinessFailure);
        await using var verify = db.Create();
        (await verify.Set<Account>().CountAsync()).ShouldBe(0);
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_OfAValue_WhenTheOperationSucceeds_CommitsAndReturnsTheValue()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                var account = NewAccount("kept@example.com");
                await s.Accounts.AddAsync(account, ct);
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success(account.Id);
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork));

        await using var verify = db.Create();
        (await verify.Set<Account>().SingleAsync()).Id.ShouldBe(result.Value);
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_Joined_ReturnsAFailureUnchanged_AndTheOwnerDecides()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        await unitOfWork.ExecuteAsync(
            static async (s, ct) =>
            {
                await s.Accounts.AddAsync(NewAccount("owner@example.com"), ct);
                await s.UnitOfWork.CommitAsync(ct);
                var inner = await s.UnitOfWork.TryExecuteAsync(static (_, _) => Task.FromResult(Result.Failure(BusinessFailure)), 0, ct);
                inner.Error.ShouldBeSameAs(BusinessFailure);
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork));

        await using var verify = db.Create();
        (await verify.Set<Account>().CountAsync()).ShouldBe(1, "the owner committed");
    }

    // ---- Transactions -------------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task TryExecuteAsync_WhenTheFirstAttemptFailsTransiently_RetriesThenSucceeds()
    {
        await using var db = await CreateDatabaseAsync();
        var failing = new FailingInsertInterceptor(failures: 1);
        await using var context = db.Create(o => o.AddInterceptors(failing), maxRetryCount: 3);
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);
        var id = new AccountId(Guid.NewGuid());

        var result = await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                await s.Accounts.AddAsync(new Account { Id = s.Id, Email = "retried@example.com" }, ct);
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork, Id: id));

        result.IsSuccess.ShouldBeTrue();
        failing.Thrown.ShouldBe(1, "the transient failure reached the execution strategy, which retried");
        await using var verify = db.Create();
        (await verify.Set<Account>().SingleAsync()).Id.ShouldBe(id);
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_WhenTheRetriesAreExhausted_PropagatesUnconverted()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create(o => o.AddInterceptors(new FailingInsertInterceptor(failures: 10)), maxRetryCount: 2);
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var thrown = await Should.ThrowAsync<PersistenceException>(async () => await unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                await s.Accounts.AddAsync(NewAccount("never@example.com"), ct);
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork)));

        thrown.InnerException.ShouldBeOfType<RetryLimitExceededException>();
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_Joined_PropagatesTheOriginalException_AndTheOwnerRollsBack()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);
        PersistenceException? seenInside = null;

        var thrown = await Should.ThrowAsync<PersistenceException>(() => unitOfWork.ExecuteAsync(
            async (s, ct) =>
            {
                await s.Accounts.AddAsync(NewAccount("dup@example.com"), ct);
                await s.UnitOfWork.CommitAsync(ct);
                try
                {
                    await s.UnitOfWork.TryExecuteAsync(
                        static async (inner, token) =>
                        {
                            await inner.Accounts.AddAsync(NewAccount("dup@example.com"), token);
                            await inner.UnitOfWork.CommitAsync(token);
                            return Result.Success();
                        },
                        s,
                        ct);
                }
                catch (PersistenceException ex)
                {
                    seenInside = ex;
                    throw;
                }
            },
            (Accounts: Accounts(context), UnitOfWork: unitOfWork)));

        thrown.ShouldBeSameAs(seenInside, "the joined call converts nothing");
        thrown.InnerException.ShouldBeAssignableTo<DbUpdateException>();
        await using var verify = db.Create();
        (await verify.Set<Account>().CountAsync()).ShouldBe(0, "the owner rolled back");
    }

    [DockerRequiredFact]
    public async Task TryExecuteAsync_WhenTheOperationSavesDirectly_ConvertsTheEfCoreException()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);

        var result = await unitOfWork.TryExecuteAsync(
            static async (c, ct) =>
            {
                c.Add(new AuditEntry { Id = Guid.NewGuid(), Text = "same" });
                c.Add(new AuditEntry { Id = Guid.NewGuid(), Text = "same" });
#pragma warning disable MKP003 // the direct flush is the entry path under test
                await c.SaveChangesAsync(ct);
#pragma warning restore MKP003
                return Result.Success();
            },
            context);

        result.Error.Code.Value.ShouldBe("PERSISTENCE.DUPLICATE");
    }

    [DockerRequiredFact]
    public async Task TryCommitAsync_OutsideATransaction_ConvertsAUniqueViolation()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);
        await Accounts(context).AddAsync(NewAccount("one@example.com"));
        (await unitOfWork.TryCommitAsync()).IsSuccess.ShouldBeTrue();

        await Accounts(context).AddAsync(NewAccount("one@example.com"));
        var result = await unitOfWork.TryCommitAsync();

        result.Error.ShouldBe(PersistenceErrors.Duplicate("ACCOUNT"));
    }

    [DockerRequiredFact]
    public async Task TryCommitAsync_InsideAnOpenTransaction_IsRefused()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create();
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () => await unitOfWork.TryCommitAsync());

        thrown.Message.ShouldContain("refused inside an open transaction");
    }

    [DockerRequiredTheory]
    [InlineData(null, "An ambient transaction has been detected")]
    [InlineData(3, "does not support user-initiated transactions")]
    public async Task AnAmbientTransaction_IsRejected(int? maxRetryCount, string message)
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create(maxRetryCount: maxRetryCount);
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        var throughExecute = await Should.ThrowAsync<InvalidOperationException>(() => unitOfWork.ExecuteAsync(
            static (_, _) => Task.CompletedTask, 0));
        var throughTryExecute = await Should.ThrowAsync<InvalidOperationException>(async () => await unitOfWork.TryExecuteAsync(
            static (_, _) => Task.FromResult(Result.Success()), 0));

        throughExecute.Message.ShouldContain(message);
        throughTryExecute.Message.ShouldContain(message);
    }

    // ---- Registration -------------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task TheProviderRegistration_ConfiguresItsClassifier_AndAddUnitOfWorkRegistersTheContract()
    {
        await using var db = await CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddMicroKitPersistence(p => p.AddEntityFrameworkCore(ef =>
        {
            Register(ef, db.Name);
            ef.AddUnitOfWork<ResultsDbContext>();
        }));
        await using var root = services.BuildServiceProvider(validateScopes: true);
        await using var scope = root.CreateAsyncScope();
        var resultUnitOfWork = scope.ServiceProvider.GetRequiredService<IResultUnitOfWork>();
        resultUnitOfWork.ShouldBeSameAs(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        var context = scope.ServiceProvider.GetRequiredService<ResultsDbContext>();

        var result = await resultUnitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                await s.Accounts.AddAsync(NewAccount("di@example.com"), ct);
                await s.UnitOfWork.CommitAsync(ct);
                await s.Accounts.AddAsync(NewAccount("di@example.com"), ct);
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Accounts: Accounts(context), UnitOfWork: scope.ServiceProvider.GetRequiredService<IUnitOfWork>()));

        result.Error.ShouldBe(PersistenceErrors.Duplicate("ACCOUNT"));
    }

    [DockerRequiredFact]
    public async Task WithoutAClassifier_OnlyConcurrencyIsConverted()
    {
        await using var db = await CreateDatabaseAsync();
        await using var context = db.Create(classifier: false);
        var unitOfWork = new EfUnitOfWork<ResultsDbContext>(context);
        await Accounts(context).AddAsync(NewAccount("one@example.com"));
        await unitOfWork.CommitAsync();
        await Accounts(context).AddAsync(NewAccount("one@example.com"));

        await Should.ThrowAsync<PersistenceException>(async () => await unitOfWork.TryCommitAsync());
    }

    // ---- The model ----------------------------------------------------------------------------

    [DockerRequiredFact]
    public void TwoRootsWithTheSameName_FailModelBuilding()
    {
        using var context = new ClashingNamesDbContext(Options<ClashingNamesDbContext>("unused"));

        var thrown = Should.Throw<InvalidOperationException>(() => _ = context.Model);

        thrown.Message.ShouldContain("both resolve to the error name 'INVOICE'");
        thrown.Message.ShouldContain("HasAggregateErrorName");
    }

    [DockerRequiredFact]
    public void TheModelAnnotation_ResolvesTwoRootsWithTheSameName()
    {
        using var context = new ResolvedNamesDbContext(Options<ResolvedNamesDbContext>("unused"));

        context.Model.FindEntityType(typeof(BillingModel.Invoice))!.FindAnnotation("MicroKit:ErrorName")!.Value.ShouldBe("BILLING_INVOICE");
        context.Model.FindEntityType(typeof(ShopModel.Invoice))!.FindAnnotation("MicroKit:ErrorName")!.Value.ShouldBe("INVOICE");
    }

    [DockerRequiredFact]
    public void AnErrorNameOnAChild_FailsModelBuilding()
    {
        using var context = new ChildNamedDbContext(Options<ChildNamedDbContext>("unused"));

        var thrown = Should.Throw<InvalidOperationException>(() => _ = context.Model);

        thrown.Message.ShouldContain("'AccountNote' has the error name 'NOTE'");
        thrown.Message.ShouldContain("not the top-most aggregate root");
    }

    [Fact]
    public void AMalformedErrorName_IsRejected()
    {
        var builder = new ModelBuilder();

        Should.Throw<ArgumentException>(() => builder.Entity<Account>().HasAggregateErrorName("shop-account"));
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static Account NewAccount(string email) => new() { Id = new AccountId(Guid.NewGuid()), Email = email };

    private static Consignment NewConsignment(AccountId accountId, string reference, int weight = 1) =>
        new() { Id = new ConsignmentId(Guid.NewGuid()), AccountId = accountId, Reference = reference, Weight = weight };

    private static EfRepository<Account, AccountId, ResultsDbContext> Accounts(ResultsDbContext context) => new(context);

    private static EfRepository<Consignment, ConsignmentId, ResultsDbContext> Consignments(ResultsDbContext context) => new(context);

    private static async Task<(AccountId Account, ConsignmentId Consignment)> SeedAccountWithConsignmentAsync(Database db)
    {
        var account = NewAccount("seed@example.com");
        account.Notes.Add(new AccountNote { Text = "seeded" });
        var shipment = NewConsignment(account.Id, "SEEDED");
        await using var context = db.Create();
        await Accounts(context).AddAsync(account);
        await Consignments(context).AddAsync(shipment);
        await new EfUnitOfWork<ResultsDbContext>(context).CommitAsync();
        return (account.Id, shipment.Id);
    }

    private DbContextOptions<TContext> Options<TContext>(
        string database,
        Action<DbContextOptionsBuilder>? configure = null,
        int? maxRetryCount = null,
        bool classifier = true)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        UseProvider(builder, database, maxRetryCount);
        configure?.Invoke(builder);
        if (classifier)
            builder.UseMicroKitPersistence(Classifier);
        else
            builder.UseMicroKitPersistence();
        return builder.Options;
    }

    private async Task<Database> CreateDatabaseAsync()
    {
        var db = new Database(this, "results_" + Guid.NewGuid().ToString("N"));
        await using var context = db.Create();
        await context.Database.EnsureCreatedAsync();
        return db;
    }

    /// <summary>One database of the provider, dropped on dispose.</summary>
    private sealed class Database(PersistenceResultScenarios owner, string name) : IAsyncDisposable
    {
        public string Name { get; } = name;

        public ResultsDbContext Create(
            Action<DbContextOptionsBuilder>? configure = null,
            int? maxRetryCount = null,
            bool classifier = true) =>
            new(owner.Options<ResultsDbContext>(Name, configure, maxRetryCount, classifier));

        public async ValueTask DisposeAsync()
        {
            await using var context = Create();
            await context.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>
    /// Fails the save with a concurrency conflict whose entries are the changed ones <c>select</c>
    /// keeps: the shape EF Core gives a conflict, with entries chosen by the test.
    /// </summary>
    private sealed class ConflictingSaveInterceptor(Func<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry, bool> select)
        : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context!.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Where(select)
#pragma warning disable EF1001 // the internal entry is the IUpdateEntry EF Core itself reports
                .Select(e => (IUpdateEntry)e.GetInfrastructure())
#pragma warning restore EF1001
                .ToList();
            throw new DbUpdateConcurrencyException("Simulated conflict.", entries);
        }
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class PersistenceResultPostgreSqlTests(PostgreSqlFixture fixture) : PersistenceResultScenarios
{
    protected override IDatabaseFailureClassifier Classifier => PostgreSqlFailureClassifier.Instance;

    protected override bool ForeignKeyOnACheckedTableIsConverted => true;

    protected override void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null) =>
        builder.UseNpgsql(ConnectionString(database), npgsql =>
        {
            if (maxRetryCount is { } retries)
                npgsql.ExecutionStrategy(d => new MarkerRetryingExecutionStrategy(d, retries));
        });

    protected override void Register(EfCoreBuilder builder, string database) =>
        builder.UsePostgreSQL<ResultsDbContext>(ConnectionString(database));

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database }.ConnectionString;
}

[Collection(SqlServerSuite.Name)]
public sealed class PersistenceResultSqlServerTests(SqlServerFixture fixture) : PersistenceResultScenarios
{
    protected override IDatabaseFailureClassifier Classifier => SqlServerFailureClassifier.Instance;

    protected override bool ForeignKeyOnACheckedTableIsConverted => false;

    protected override void UseProvider(DbContextOptionsBuilder builder, string database, int? maxRetryCount = null) =>
        builder.UseSqlServer(ConnectionString(database), sql =>
        {
            if (maxRetryCount is { } retries)
                sql.ExecutionStrategy(d => new MarkerRetryingExecutionStrategy(d, retries));
        });

    protected override void Register(EfCoreBuilder builder, string database) =>
        builder.UseSqlServer<ResultsDbContext>(ConnectionString(database));

    private string ConnectionString(string database) =>
        new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = database }.ConnectionString;
}

public readonly record struct AccountId(Guid Value) : IEntityId;

public readonly record struct ConsignmentId(Guid Value) : IEntityId;

/// <summary>An aggregate with a unique e-mail and a child collection.</summary>
public sealed class Account : IAggregateRoot
{
    public AccountId Id { get; init; }

    public string Email { get; set; } = string.Empty;

    public List<AccountNote> Notes { get; } = [];
}

public sealed class AccountNote
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Another aggregate, named <c>DELIVERY</c> in the codes, referencing an account (Restrict by convention),
/// with a unique reference and a declared check constraint.
/// </summary>
public sealed class Consignment : IAggregateRoot
{
    public ConsignmentId Id { get; init; }

    public AccountId AccountId { get; init; }

    public string Reference { get; set; } = string.Empty;

    public int Weight { get; set; }
}

/// <summary>An entity outside every aggregate, with a unique text.</summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; }

    public string Text { get; set; } = string.Empty;
}

public class ResultsDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion(id => id.Value, v => new AccountId(v)).ValueGeneratedNever();
            b.HasIndex(a => a.Email).IsUnique();
            b.HasMany(a => a.Notes).WithOne().HasForeignKey("AccountId").IsRequired();
        });
        modelBuilder.Entity<AccountNote>().Property(n => n.Id).ValueGeneratedNever();
        modelBuilder.Entity<Consignment>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasConversion(id => id.Value, v => new ConsignmentId(v)).ValueGeneratedNever();
            b.Property(s => s.AccountId).HasConversion(id => id.Value, v => new AccountId(v));
            b.HasOne<Account>().WithMany().HasForeignKey(s => s.AccountId);
            b.HasIndex(s => s.Reference).IsUnique();
            b.ToTable(t => t.HasCheckConstraint("CK_Consignment_Weight", Database.IsSqlServer() ? "[Weight] > 0" : "\"Weight\" > 0"));
            b.HasAggregateErrorName("DELIVERY");
        });
        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.Property(a => a.Id).ValueGeneratedNever();
            b.HasIndex(a => a.Text).IsUnique();
        });
    }
}

public static class BillingModel
{
    public sealed class Invoice : IAggregateRoot
    {
        public Guid Id { get; init; }
    }
}

public static class ShopModel
{
    public sealed class Invoice : IAggregateRoot
    {
        public Guid Id { get; init; }
    }
}

public sealed class ClashingNamesDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingModel.Invoice>().ToTable("billing_invoices");
        modelBuilder.Entity<ShopModel.Invoice>().ToTable("shop_invoices");
    }
}

public sealed class ResolvedNamesDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingModel.Invoice>().ToTable("billing_invoices").HasAggregateErrorName("BILLING_INVOICE");
        modelBuilder.Entity<ShopModel.Invoice>().ToTable("shop_invoices");
    }
}

public sealed class ChildNamedDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(b =>
        {
            b.Property(a => a.Id).HasConversion(id => id.Value, v => new AccountId(v));
            b.HasMany(a => a.Notes).WithOne().HasForeignKey("AccountId");
        });
        modelBuilder.Entity<AccountNote>().HasAggregateErrorName("NOTE");
    }
}
