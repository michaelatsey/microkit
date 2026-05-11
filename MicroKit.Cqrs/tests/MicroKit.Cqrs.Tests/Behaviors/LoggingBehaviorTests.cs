using MediatR;
using MicroKit.Abstractions.Contexts;
using MicroKit.Cqrs.MediatR.Behaviors;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroKit.Cqrs.Tests.Behaviors;

public sealed class LoggingBehaviorTests
{
    private readonly ICorrelationContext _correlation = Substitute.For<ICorrelationContext>();
    private readonly ITenantIdAccessor _tenant = Substitute.For<ITenantIdAccessor>();

    private LoggingBehavior<TestRequest, string> CreateBehavior()
        => new(NullLogger<LoggingBehavior<TestRequest, string>>.Instance, _correlation, _tenant);

    [Fact]
    public async Task Handle_SuccessfulNext_ReturnsResult()
    {
        _correlation.CorrelationId.Returns("corr-1");
        _tenant.TenantId.Returns("tenant-1");
        var behavior = CreateBehavior();

        var result = await behavior.Handle(new TestRequest(), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task Handle_NextThrows_RethrowsException()
    {
        _correlation.CorrelationId.Returns("corr-1");
        _tenant.TenantId.Returns((string?)null);
        var behavior = CreateBehavior();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => behavior.Handle(new TestRequest(), _ => throw new InvalidOperationException("boom"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NullTenantId_DoesNotThrow()
    {
        _correlation.CorrelationId.Returns("corr-1");
        _tenant.TenantId.Returns((string?)null);
        var behavior = CreateBehavior();

        var result = await behavior.Handle(new TestRequest(), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task Handle_PassesCancellationTokenToNext()
    {
        _correlation.CorrelationId.Returns("corr-1");
        _tenant.TenantId.Returns("t");
        using var cts = new CancellationTokenSource();
        CancellationToken captured = default;

        var behavior = CreateBehavior();
        await behavior.Handle(new TestRequest(), ct =>
        {
            captured = ct;
            return Task.FromResult("x");
        }, cts.Token);

        Assert.Equal(cts.Token, captured);
    }
}

public sealed record TestRequest : IRequest<string>;
