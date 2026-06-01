using MediatR;
using MicroKit.MediatR;
using MicroKit.Result;
using static MicroKit.Result.Result;
using NSubstitute;
using Shouldly;
using Xunit;

namespace MicroKit.MediatR.UnitTests.Extensions;

/// <summary>
/// Verifies that MediatorExtensions delegates to the correct IMediator overload
/// for each CQRS contract (void command, result-bearing command, query, stream query).
/// Tests focus on the delegation contract, not the underlying MediatR dispatch.
/// </summary>
public sealed class MediatorExtensionsTests
{
    [Fact]
    public async Task SendCommandAsync_VoidCommand_DelegatesIssuerSendToIMediator()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var command = new VoidTestCommand();

        await mediator.SendCommandAsync(command);

        await mediator.Received(1).Send(command, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendCommandAsync_ResultBearingCommand_DelegatesSendTResultToIMediator()
    {
        var mediator = Substitute.For<IMediator>();
        var expected = Success("hello");
        mediator.Send<Result<string>>(Arg.Any<IRequest<Result<string>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));
        var command = new ResultTestCommand();

        var result = await mediator.SendCommandAsync<ResultTestCommand, Result<string>>(command);

        result.ShouldBe(expected);
        await mediator.Received(1).Send<Result<string>>(command, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendQueryAsync_DelegatesSendTResultToIMediator()
    {
        var mediator = Substitute.For<IMediator>();
        var expected = Success(42);
        mediator.Send<Result<int>>(Arg.Any<IRequest<Result<int>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));
        var query = new IntTestQuery();

        var result = await mediator.SendQueryAsync<IntTestQuery, Result<int>>(query);

        result.ShouldBe(expected);
        await mediator.Received(1).Send<Result<int>>(query, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void StreamQueryAsync_DelegatesCreateStreamToIMediator()
    {
        var mediator = Substitute.For<IMediator>();
        var query = new StringStreamTestQuery();

        _ = mediator.StreamQueryAsync<StringStreamTestQuery, string>(query);

        mediator.Received(1).CreateStream<string>(query, Arg.Any<CancellationToken>());
    }

    private sealed record VoidTestCommand : ICommand;
    private sealed record ResultTestCommand : ICommand<Result<string>>;
    private sealed record IntTestQuery : IQuery<Result<int>>;
    private sealed record StringStreamTestQuery : IStreamQuery<string>;
}
