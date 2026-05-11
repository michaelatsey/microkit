using MediatR;
using MicroKit.Cqrs.Abstractions.Queries;
using MicroKit.Cqrs.MediatR.Handlers;

namespace MicroKit.Cqrs.Tests.Handlers;

/// <summary>
/// Verifies that QueryHandler bridges IQueryHandler to MediatR's IRequestHandler pipeline.
/// </summary>
public sealed class QueryHandlerBridgeTests
{
    [Fact]
    public async Task Handle_DelegatesToHandleAsync()
    {
        var handler = new UpperCaseQueryHandler();

        var result = await handler.Handle(new UpperCaseQuery("hello"), CancellationToken.None);

        Assert.Equal("HELLO", result);
    }

    [Fact]
    public async Task Handle_PassesCancellationToken()
    {
        var handler = new TokenCapturingHandler();
        using var cts = new CancellationTokenSource();

        await handler.Handle(new UpperCaseQuery("x"), cts.Token);

        Assert.Equal(cts.Token, handler.CapturedToken);
    }

    [Fact]
    public async Task Handler_ImplementsIQueryHandler()
    {
        IQueryHandler<UpperCaseQuery, string> handler = new UpperCaseQueryHandler();
        var result = await handler.HandleAsync(new UpperCaseQuery("world"));
        Assert.Equal("WORLD", result);
    }

    [Fact]
    public async Task Handler_ImplementsIRequestHandler()
    {
        IRequestHandler<UpperCaseQuery, string> handler = new UpperCaseQueryHandler();
        var result = await handler.Handle(new UpperCaseQuery("test"), CancellationToken.None);
        Assert.Equal("TEST", result);
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed record UpperCaseQuery(string Value) : IQuery<string>, IRequest<string>;

    private sealed class UpperCaseQueryHandler : QueryHandler<UpperCaseQuery, string>
    {
        public override Task<string> HandleAsync(UpperCaseQuery query, CancellationToken ct = default)
            => Task.FromResult(query.Value.ToUpperInvariant());
    }

    private sealed class TokenCapturingHandler : QueryHandler<UpperCaseQuery, string>
    {
        public CancellationToken CapturedToken { get; private set; }
        public override Task<string> HandleAsync(UpperCaseQuery query, CancellationToken ct = default)
        {
            CapturedToken = ct;
            return Task.FromResult(query.Value);
        }
    }
}
