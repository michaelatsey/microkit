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

    private sealed record TestCommand : ICommand<Result<string>>;

    private sealed record PlainCommand : ICommand<string>;

    private sealed record TestQuery : IQuery<Result<string>>;

    // Error is abstract — a concrete subtype is required to construct Result.Failure.
    private sealed record TestError() : Error(ErrorCode.From("TEST.ERROR"), "test error");

    // Hand-written rather than an NSubstitute mock: ExecuteAsync<TState, TResult> must actually
    // invoke the operation, but TransactionHandlerState is a private nested struct, so TState
    // cannot be named at the call site to configure a substitute. Mirrors EfUnitOfWork.ExecuteAsync
    // semantics — commit on success, rollback on exception.
    private sealed class FakeTransactionalContext : ITransactionalContext
    {
        public bool Entered { get; private set; }
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }

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
            try
            {
                var result = await operation(state, ct).ConfigureAwait(false);
                Committed = true;
                return result;
            }
            catch
            {
                RolledBack = true;
                throw;
            }
        }
    }
}
