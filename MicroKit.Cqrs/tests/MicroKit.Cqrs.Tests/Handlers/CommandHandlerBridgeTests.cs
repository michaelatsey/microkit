using MediatR;
using MicroKit.Cqrs.Abstractions.Commands;
using MicroKit.Cqrs.MediatR.Handlers;

namespace MicroKit.Cqrs.Tests.Handlers;

/// <summary>
/// Verifies that CommandHandler bridges ICommandHandler to MediatR's IRequestHandler pipeline.
/// </summary>
public sealed class CommandHandlerBridgeTests
{
    // ── Void command handler ───────────────────────────────────────────────────

    [Fact]
    public async Task VoidCommandHandler_Handle_DelegatesToHandleAsync()
    {
        var handler = new TrackingVoidHandler();

        await handler.Handle(new VoidCmd(), CancellationToken.None);

        Assert.True(handler.WasCalled);
    }

    [Fact]
    public async Task VoidCommandHandler_Handle_ReturnsUnitValue()
    {
        var handler = new TrackingVoidHandler();

        var result = await handler.Handle(new VoidCmd(), CancellationToken.None);

        Assert.Equal(Unit.Value, result);
    }

    [Fact]
    public async Task VoidCommandHandler_HandleAsync_PassesCancellationToken()
    {
        var handler = new TokenCapturingVoidHandler();
        using var cts = new CancellationTokenSource();

        await handler.Handle(new VoidCmd(), cts.Token);

        Assert.Equal(cts.Token, handler.CapturedToken);
    }

    [Fact]
    public async Task VoidCommandHandler_ImplementsICommandHandler()
    {
        ICommandHandler<VoidCmd> handler = new TrackingVoidHandler();
        await handler.HandleAsync(new VoidCmd());
    }

    [Fact]
    public async Task VoidCommandHandler_ImplementsIRequestHandler()
    {
        IRequestHandler<VoidCmd, Unit> handler = new TrackingVoidHandler();
        var result = await handler.Handle(new VoidCmd(), CancellationToken.None);
        Assert.Equal(Unit.Value, result);
    }

    // ── Response command handler ───────────────────────────────────────────────

    [Fact]
    public async Task ResponseCommandHandler_Handle_ReturnsExpectedValue()
    {
        var handler = new ResponseHandler();

        var result = await handler.Handle(new ResponseCmd("hello"), CancellationToken.None);

        Assert.Equal("HELLO", result);
    }

    [Fact]
    public async Task ResponseCommandHandler_ImplementsICommandHandler()
    {
        ICommandHandler<ResponseCmd, string> handler = new ResponseHandler();
        var result = await handler.HandleAsync(new ResponseCmd("world"));
        Assert.Equal("WORLD", result);
    }

    [Fact]
    public async Task ResponseCommandHandler_ImplementsIRequestHandler()
    {
        IRequestHandler<ResponseCmd, string> handler = new ResponseHandler();
        var result = await handler.Handle(new ResponseCmd("test"), CancellationToken.None);
        Assert.Equal("TEST", result);
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed record VoidCmd : ICommand, IRequest<Unit>;
    private sealed record ResponseCmd(string Value) : ICommand<string>, IRequest<string>;

    private sealed class TrackingVoidHandler : CommandHandler<VoidCmd>
    {
        public bool WasCalled { get; private set; }
        public override Task HandleAsync(VoidCmd command, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TokenCapturingVoidHandler : CommandHandler<VoidCmd>
    {
        public CancellationToken CapturedToken { get; private set; }
        public override Task HandleAsync(VoidCmd command, CancellationToken ct = default)
        {
            CapturedToken = ct;
            return Task.CompletedTask;
        }
    }

    private sealed class ResponseHandler : CommandHandler<ResponseCmd, string>
    {
        public override Task<string> HandleAsync(ResponseCmd command, CancellationToken ct = default)
            => Task.FromResult(command.Value.ToUpperInvariant());
    }
}
