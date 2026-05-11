using MediatR;
using MicroKit.Cqrs.Abstractions.Queries;
using MicroKit.Cqrs.MediatR.Queries;
using NSubstitute;

namespace MicroKit.Cqrs.Tests.Queries;

public sealed class MediatRQueryBusTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly MediatRQueryBus _bus;

    public MediatRQueryBusTests() => _bus = new MediatRQueryBus(_sender);

    [Fact]
    public async Task AskAsync_Query_DispatchesToMediatRAndReturnsResult()
    {
        var query = new TestQuery();
        _sender.Send(Arg.Any<IRequest<string>>(), Arg.Any<CancellationToken>())
               .Returns("answer");

        var result = await _bus.AskAsync(query);

        Assert.Equal("answer", result);
        await _sender.Received(1).Send(query, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAsync_Query_PassesCancellationToken()
    {
        var query = new TestQuery();
        using var cts = new CancellationTokenSource();
        _sender.Send(Arg.Any<IRequest<string>>(), Arg.Any<CancellationToken>())
               .Returns("x");

        await _bus.AskAsync(query, cts.Token);

        await _sender.Received(1).Send(query, cts.Token);
    }

    [Fact]
    public async Task AskAsync_QueryNotImplementingIRequest_ThrowsInvalidOperationException()
    {
        var query = new NonMediatRQuery();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _bus.AskAsync(query));

        Assert.Contains("IRequest<String>", ex.Message);
        Assert.Contains(nameof(NonMediatRQuery), ex.Message);
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class TestQuery : IQuery<string>, IRequest<string> { }

    /// <summary>Implements IQuery&lt;string&gt; but NOT IRequest&lt;string&gt; — simulates a mis-configured query.</summary>
    private sealed class NonMediatRQuery : IQuery<string> { }
}
