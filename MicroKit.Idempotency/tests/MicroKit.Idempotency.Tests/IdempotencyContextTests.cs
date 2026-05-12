using MicroKit.Idempotency.Abstractions.Models;
using MicroKit.Idempotency.Core.Context;

namespace MicroKit.Idempotency.Tests;

public sealed class IdempotencyContextTests
{
    private readonly IdempotencyContext _context = new();

    [Fact]
    public void BeginScope_SetsCurrentKey()
    {
        using var scope = _context.BeginScope("key-1");

        Assert.Equal("key-1", _context.CurrentKey);
        Assert.True(_context.IsIdempotent);
    }

    [Fact]
    public void BeginScope_WhenScopeAlreadyActive_Throws()
    {
        using var scope = _context.BeginScope("key-1");

        Assert.Throws<InvalidOperationException>(() => _context.BeginScope("key-2"));
    }

    [Fact]
    public void Dispose_ClearsCurrentKey()
    {
        var scope = _context.BeginScope("key-1");
        scope.Dispose();

        Assert.Null(_context.CurrentKey);
        Assert.False(_context.IsIdempotent);
    }

    [Fact]
    public void UpdateState_WithinScope_ReflectsCurrentStatus()
    {
        using var scope = _context.BeginScope("key-1");
        var state = new IdempotencyState("key-1", "tenant-1", IdempotencyStatus.Completed);

        _context.UpdateState(state);

        Assert.Equal(IdempotencyStatus.Completed, _context.CurrentStatus);
    }

    [Fact]
    public void UpdateState_OutsideScope_DoesNotThrow()
    {
        var state = new IdempotencyState("key-1", "tenant-1", IdempotencyStatus.Completed);

        var ex = Record.Exception(() => _context.UpdateState(state));

        Assert.Null(ex);
    }

    [Fact]
    public void CurrentStatus_NoScope_ReturnsNull()
    {
        Assert.Null(_context.CurrentStatus);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var scope = _context.BeginScope("key-1");
        scope.Dispose();
        scope.Dispose(); // must not throw

        Assert.Null(_context.CurrentKey);
    }
}
