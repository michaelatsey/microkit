using MicroKit.Idempotency.Abstractions.Models;
using MicroKit.Idempotency.Core.Context;

namespace MicroKit.Idempotency.Tests;

/// <summary>Unit tests for <see cref="IdempotencyContext"/>.</summary>
public sealed class IdempotencyContextTests
{
    private readonly IdempotencyContext _context = new();

    /// <summary>Verifies that beginning a scope sets the current key.</summary>
    [Fact]
    public void BeginScope_SetsCurrentKey()
    {
        using var scope = _context.BeginScope("key-1");

        Assert.Equal("key-1", _context.CurrentKey);
        Assert.True(_context.IsIdempotent);
    }

    /// <summary>Verifies that beginning a second scope while one is active throws.</summary>
    [Fact]
    public void BeginScope_WhenScopeAlreadyActive_Throws()
    {
        using var scope = _context.BeginScope("key-1");

        Assert.Throws<InvalidOperationException>(() => _context.BeginScope("key-2"));
    }

    /// <summary>Verifies that disposing a scope clears the current key.</summary>
    [Fact]
    public void Dispose_ClearsCurrentKey()
    {
        var scope = _context.BeginScope("key-1");
        scope.Dispose();

        Assert.Null(_context.CurrentKey);
        Assert.False(_context.IsIdempotent);
    }

    /// <summary>Verifies that updating state within a scope is reflected in the current status.</summary>
    [Fact]
    public void UpdateState_WithinScope_ReflectsCurrentStatus()
    {
        using var scope = _context.BeginScope("key-1");
        var state = new IdempotencyState("key-1", "tenant-1", IdempotencyStatus.Completed);

        _context.UpdateState(state);

        Assert.Equal(IdempotencyStatus.Completed, _context.CurrentStatus);
    }

    /// <summary>Verifies that updating state outside a scope does not throw.</summary>
    [Fact]
    public void UpdateState_OutsideScope_DoesNotThrow()
    {
        var state = new IdempotencyState("key-1", "tenant-1", IdempotencyStatus.Completed);

        var ex = Record.Exception(() => _context.UpdateState(state));

        Assert.Null(ex);
    }

    /// <summary>Verifies that the current status is null when there is no active scope.</summary>
    [Fact]
    public void CurrentStatus_NoScope_ReturnsNull()
    {
        Assert.Null(_context.CurrentStatus);
    }

    /// <summary>Verifies that disposing a scope multiple times does not throw.</summary>
    [Fact]
    public void Dispose_IsIdempotent()
    {
        var scope = _context.BeginScope("key-1");
        scope.Dispose();
        scope.Dispose(); // must not throw

        Assert.Null(_context.CurrentKey);
    }
}
