using MediatR;
using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Cqrs.MediatR.Commands;
using NSubstitute;

namespace MicroKit.Cqrs.Tests.Commands;

public sealed class MediatRCommandBusTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly MediatRCommandBus _bus;

    public MediatRCommandBusTests() => _bus = new MediatRCommandBus(_sender);

    // ── Void commands ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_VoidCommand_DispatchesToMediatR()
    {
        var command = new TestVoidCommand();
        _sender.Send(Arg.Any<IRequest<Unit>>(), Arg.Any<CancellationToken>())
               .Returns(Unit.Value);

        await _bus.SendAsync(command);

        await _sender.Received(1).Send(command, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_VoidCommand_PassesCancellationToken()
    {
        var command = new TestVoidCommand();
        using var cts = new CancellationTokenSource();
        _sender.Send(Arg.Any<IRequest<Unit>>(), Arg.Any<CancellationToken>())
               .Returns(Unit.Value);

        await _bus.SendAsync(command, cts.Token);

        await _sender.Received(1).Send(command, cts.Token);
    }

    [Fact]
    public async Task SendAsync_CommandNotImplementingIRequestUnit_ThrowsInvalidOperationException()
    {
        var command = new NonMediatRCommand();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _bus.SendAsync(command));

        Assert.Contains("IRequest<Unit>", ex.Message);
        Assert.Contains(nameof(NonMediatRCommand), ex.Message);
    }

    // ── Response commands ──────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_ResponseCommand_DispatchesToMediatRAndReturnsResult()
    {
        var command = new TestResponseCommand();
        _sender.Send(Arg.Any<IRequest<string>>(), Arg.Any<CancellationToken>())
               .Returns("result");

        var result = await _bus.SendAsync<string>(command);

        Assert.Equal("result", result);
        await _sender.Received(1).Send(command, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ResponseCommand_PassesCancellationToken()
    {
        var command = new TestResponseCommand();
        using var cts = new CancellationTokenSource();
        _sender.Send(Arg.Any<IRequest<string>>(), Arg.Any<CancellationToken>())
               .Returns("x");

        await _bus.SendAsync<string>(command, cts.Token);

        await _sender.Received(1).Send(command, cts.Token);
    }

    [Fact]
    public async Task SendAsync_ResponseCommandNotImplementingIRequest_ThrowsInvalidOperationException()
    {
        var command = new NonMediatRResponseCommand();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _bus.SendAsync<string>(command));

        Assert.Contains("IRequest<String>", ex.Message);
        Assert.Contains(nameof(NonMediatRResponseCommand), ex.Message);
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class TestVoidCommand : ICommand, IRequest<Unit> { }

    private sealed class TestResponseCommand : ICommand<string>, IRequest<string> { }

    /// <summary>Implements ICommand but NOT IRequest&lt;Unit&gt; — simulates a mis-configured command.</summary>
    private sealed class NonMediatRCommand : ICommand { }

    /// <summary>Implements ICommand&lt;string&gt; but NOT IRequest&lt;string&gt;.</summary>
    private sealed class NonMediatRResponseCommand : ICommand<string> { }
}
