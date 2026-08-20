using MediatR;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors;
using MicroKit.MediatR.Events;
using MicroKit.Persistence.Abstractions;
using MicroKit.Result;
using static MicroKit.Result.Result;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.UnitTests.Behaviors;

public sealed class TransactionBehaviorTests
{
    // The regression test for the silent no-write defect: the transaction used to commit without
    // anyone calling SaveChangesAsync, and the outbox rows staged by the dispatch were discarded.
    // Ordering is load-bearing — flushing before the dispatch would drop every outbox row.
    [Fact]
    public async Task Handle_WhenCommandSucceeds_CommitsAfterDispatchingEvents()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Success("ok"));
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        var result = await behavior.Handle(new TestCommand(), next, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Received.InOrder(() =>
        {
            dispatcher.DispatchEventsAsync(Arg.Any<CancellationToken>());
            unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
        unitOfWork.DidNotReceive().DiscardChanges();
    }

    [Fact]
    public async Task Handle_WhenCommandSucceeds_CallsCommitExactlyOnce()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Success("ok"));
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        await behavior.Handle(new TestCommand(), next, CancellationToken.None);

        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        tx.Committed.ShouldBeTrue();
        unitOfWork.DidNotReceive().DiscardChanges();
    }

    // A committed unit of work must never be discarded: the discard belongs in a catch, not a
    // finally. This test is the named guard against a finally-shaped regression.
    [Fact]
    public async Task Handle_WhenCommandSucceeds_DoesNotDiscard()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Success("ok"));
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        var result = await behavior.Handle(new TestCommand(), next, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        unitOfWork.DidNotReceive().DiscardChanges();
    }

    [Fact]
    public async Task Handle_WhenResultIsFailure_DoesNotDispatchAndDoesNotCommit()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Failure<string>(new TestError()));
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        var result = await behavior.Handle(new TestCommand(), next, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await dispatcher.DidNotReceive().DispatchEventsAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        unitOfWork.Received(1).DiscardChanges();
    }

    // First non-commit exit (ADR-005). Nothing throws, so the database transaction commits clean —
    // if the change tracker is not cleared here, nothing else ever clears it, and the next command
    // in the same scoped DbContext writes the failed command's entities.
    [Fact]
    public async Task Handle_WhenResultIsFailure_DiscardsExactlyOnce()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var error = new TestError();
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Failure<string>(error));
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        var result = await behavior.Handle(new TestCommand(), next, CancellationToken.None);

        unitOfWork.Received(1).DiscardChanges();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        tx.Committed.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenRequestIsNotACommand_PassesThroughWithoutTransaction()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var callCount = 0;
        RequestHandlerDelegate<Result<string>> next = () => { callCount++; return Task.FromResult(Success("ok")); };
        var behavior = new TransactionBehavior<TestQuery, Result<string>>(tx, dispatcher, unitOfWork);

        var result = await behavior.Handle(new TestQuery(), next, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        callCount.ShouldBe(1);
        tx.Entered.ShouldBeFalse();
        await dispatcher.DidNotReceive().DispatchEventsAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        unitOfWork.DidNotReceive().DiscardChanges();
    }

    [Fact]
    public async Task Handle_WhenHandlerThrows_DoesNotCommitAndPropagates()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        RequestHandlerDelegate<Result<string>> next = () => throw new InvalidOperationException("handler failed");
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new TestCommand(), next, CancellationToken.None));

        ex.Message.ShouldContain("handler failed");
        tx.RolledBack.ShouldBeTrue();
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        unitOfWork.Received(1).DiscardChanges();
    }

    // Second non-commit exit (ADR-005). The transaction rollback below undoes the database work but
    // does NOT reset the change tracker — the staged entities survive a rollback exactly as they
    // survive a business failure. The rethrow must be bare: same instance, original stack.
    [Fact]
    public async Task Handle_WhenHandlerThrows_DiscardsAndRethrowsSameException()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var thrown = new InvalidOperationException("handler failed");
        RequestHandlerDelegate<Result<string>> next = () => throw thrown;
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new TestCommand(), next, CancellationToken.None));

        ex.ShouldBeSameAs(thrown);
        // The throwing frame is the lambda above; the compiler embeds this method's name in it.
        ex.StackTrace.ShouldNotBeNull();
        ex.StackTrace.ShouldContain(nameof(Handle_WhenHandlerThrows_DiscardsAndRethrowsSameException));
        unitOfWork.Received(1).DiscardChanges();
    }

    // The discard must run INSIDE the ExecuteAsync operation (the retry-attempt boundary), not
    // around it: at the moment it is called, the transaction has not rolled back yet.
    [Fact]
    public async Task Handle_WhenHandlerThrows_DiscardsBeforeTheTransactionRollsBack()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        // Seeded true so a discard that never happens also fails this assertion.
        var rolledBackWhenDiscarded = true;
        unitOfWork.When(u => u.DiscardChanges()).Do(_ => rolledBackWhenDiscarded = tx.RolledBack);
        RequestHandlerDelegate<Result<string>> next = () => throw new InvalidOperationException("handler failed");
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new TestCommand(), next, CancellationToken.None));

        rolledBackWhenDiscarded.ShouldBeFalse();
        tx.RolledBack.ShouldBeTrue();
    }

    // ADR-MEDIATR-012's central claim, pinned. The discard sits INSIDE the ExecuteAsync operation,
    // so a provider execution strategy that replays after a transient failure starts every attempt
    // from a clean change set. Moving the try/catch around ExecuteAsync would discard once per
    // command instead of once per attempt: the replay would inherit the first attempt's staged
    // entities, and — since the command ultimately succeeds — no discard would happen at all.
    [Fact]
    public async Task Handle_WhenOperationIsReplayed_DiscardsBetweenAttempts()
    {
        var tx = new FakeTransactionalContext(replayOnce: true);
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var timeline = new List<string>();
        var attempt = 0;
        RequestHandlerDelegate<Result<string>> next = () =>
        {
            timeline.Add($"handler:{++attempt}");
            if (attempt == 1)
                throw new InvalidOperationException("transient failure");
            return Task.FromResult(Success("ok"));
        };
        unitOfWork.When(u => u.DiscardChanges()).Do(_ => timeline.Add("discard"));
        unitOfWork.When(u => u.CommitAsync(Arg.Any<CancellationToken>())).Do(_ => timeline.Add("commit"));
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        var result = await behavior.Handle(new TestCommand(), next, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        tx.Attempts.ShouldBe(2);
        // The discard falls BETWEEN the two handler calls — attempt 2 begins from a clean change set.
        timeline.ShouldBe(ExpectedReplayTimeline);
    }

    [Fact]
    public async Task Handle_WhenDispatchThrows_DoesNotCommit()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        dispatcher.DispatchEventsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("dispatch failed")));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Success("ok"));
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new TestCommand(), next, CancellationToken.None));

        tx.RolledBack.ShouldBeTrue();
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        unitOfWork.Received(1).DiscardChanges();
    }

    // The dispatch stages outbox rows in the same change tracker as the aggregates: a failed
    // dispatch leaves both behind.
    [Fact]
    public async Task Handle_WhenDispatchThrows_Discards()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        dispatcher.DispatchEventsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("dispatch failed")));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Success("ok"));
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new TestCommand(), next, CancellationToken.None));

        unitOfWork.Received(1).DiscardChanges();
        Received.InOrder(() =>
        {
            dispatcher.DispatchEventsAsync(Arg.Any<CancellationToken>());
            unitOfWork.DiscardChanges();
        });
    }

    // ADR-005 names this the more common failure mode for the persistence layer: a failing flush
    // (PersistenceException wrapping DbUpdateConcurrencyException / DbUpdateException).
    [Fact]
    public async Task Handle_WhenCommitThrows_Discards()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var commitFailure = new InvalidOperationException("commit failed");
        unitOfWork.CommitAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromException(commitFailure));
        RequestHandlerDelegate<Result<string>> next = () => Task.FromResult(Success("ok"));
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new TestCommand(), next, CancellationToken.None));

        ex.ShouldBeSameAs(commitFailure);
        tx.RolledBack.ShouldBeTrue();
        unitOfWork.Received(1).DiscardChanges();
        Received.InOrder(() =>
        {
            unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
            unitOfWork.DiscardChanges();
        });
    }

    // Cancellation is a non-commit exit like any other.
    [Fact]
    public async Task Handle_WhenHandlerCancelled_DiscardsAndPropagatesCancellation()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        RequestHandlerDelegate<Result<string>> next = () => throw new OperationCanceledException(cts.Token);
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        await Should.ThrowAsync<OperationCanceledException>(
            () => behavior.Handle(new TestCommand(), next, cts.Token));

        unitOfWork.Received(1).DiscardChanges();
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    // The discard inside the catch block is guarded: an exception raised by cleanup inside a catch
    // replaces the in-flight exception and destroys the diagnosis of the original failure.
    // Unreachable on EF Core, reachable on any other IUnitOfWork implementation.
    [Fact]
    public async Task Handle_WhenDiscardThrows_PropagatesOriginalException()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var handlerFailure = new InvalidOperationException("handler failed");
        var discardFailure = new InvalidOperationException("discard failed");
        unitOfWork.When(u => u.DiscardChanges()).Do(_ => throw discardFailure);
        RequestHandlerDelegate<Result<string>> next = () => throw handlerFailure;
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(tx, dispatcher, unitOfWork);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.Handle(new TestCommand(), next, CancellationToken.None));

        ex.ShouldBeSameAs(handlerFailure);
        ex.ShouldNotBeSameAs(discardFailure);
        unitOfWork.Received(1).DiscardChanges();
    }

    // ResultInspector returns false for a non-Result<T> TResponse, so the success path must still run.
    [Fact]
    public async Task Handle_WhenResponseIsNotResultType_DispatchesAndCommits()
    {
        var tx = new FakeTransactionalContext();
        var dispatcher = Substitute.For<IDomainEventsDispatcher>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        RequestHandlerDelegate<string> next = () => Task.FromResult("plain");
        var behavior = new TransactionBehavior<PlainCommand, string>(tx, dispatcher, unitOfWork);

        var result = await behavior.Handle(new PlainCommand(), next, CancellationToken.None);

        result.ShouldBe("plain");
        Received.InOrder(() =>
        {
            dispatcher.DispatchEventsAsync(Arg.Any<CancellationToken>());
            unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
        unitOfWork.DidNotReceive().DiscardChanges();
    }

    [Fact]
    public void Order_IsPipelineOrderTransaction()
    {
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(
            new FakeTransactionalContext(),
            Substitute.For<IDomainEventsDispatcher>(),
            Substitute.For<IUnitOfWork>());

        behavior.Order.ShouldBe(PipelineOrder.Transaction);
    }

    // Hoisted to a field for CA1861 — the assertion is a sequence, not a set: the discard must fall
    // between the two handler calls, not merely appear somewhere in the timeline.
    private static readonly string[] ExpectedReplayTimeline = ["handler:1", "discard", "handler:2", "commit"];

    private sealed record TestCommand : ICommand<Result<string>>;

    private sealed record PlainCommand : ICommand<string>;

    private sealed record TestQuery : IQuery<Result<string>>;

    // Error is abstract — a concrete subtype is required to construct Result.Failure.
    private sealed record TestError() : Error(ErrorCode.From("TEST.ERROR"), "test error");

    // Hand-written rather than an NSubstitute mock: ExecuteAsync<TState, TResult> must actually
    // invoke the operation, but TransactionHandlerState is a private nested struct, so TState
    // cannot be named at the call site to configure a substitute. Mirrors EfUnitOfWork.ExecuteAsync
    // semantics — commit on success, rollback on exception.
    // replayOnce models a provider execution strategy: the first attempt fails transiently, the
    // whole operation is rolled back and replayed once. Only the TResult overload models replay —
    // it is the only one TransactionBehavior calls.
    private sealed class FakeTransactionalContext(bool replayOnce = false) : ITransactionalContext
    {
        public bool Entered { get; private set; }
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }
        public int Attempts { get; private set; }

        public async Task ExecuteAsync<TState>(
            Func<TState, CancellationToken, Task> operation,
            TState state,
            CancellationToken ct = default)
        {
            Entered = true;
            try
            {
                await operation(state, ct).ConfigureAwait(false);
                Committed = true;
            }
            catch
            {
                RolledBack = true;
                throw;
            }
        }

        public async Task<TResult> ExecuteAsync<TState, TResult>(
            Func<TState, CancellationToken, Task<TResult>> operation,
            TState state,
            CancellationToken ct = default)
        {
            Entered = true;
            while (true)
            {
                Attempts++;
                try
                {
                    var result = await operation(state, ct).ConfigureAwait(false);
                    Committed = true;
                    return result;
                }
                catch when (replayOnce && Attempts == 1)
                {
                    // Transient failure on the first attempt: roll back, then replay the whole
                    // operation — exactly what EfUnitOfWork's CreateExecutionStrategy() does.
                    RolledBack = true;
                }
                catch
                {
                    RolledBack = true;
                    throw;
                }
            }
        }
    }
}
