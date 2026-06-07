namespace MicroKit.Auth;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed implementation of <see cref="ICurrentUserAccessor"/>.
/// The current user flows down the async call chain and changes in child tasks do not
/// propagate back to the parent scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registration constraint:</b> must be registered as <b>Scoped</b>, never as Singleton.
/// A Singleton registration would share the same instance across DI scopes, causing user
/// context to leak between concurrent requests.
/// </para>
/// <para>
/// <b>ExecutionContext capture timing:</b> <c>AsyncLocal&lt;T&gt;</c> values are captured
/// at the point where a new execution branch is scheduled — for example when
/// <c>Task.Run()</c> or <c>Parallel.ForEachAsync</c> is called. A <see cref="Set"/> call
/// made <em>after</em> scheduling a child task writes into the parent's context only; the
/// child task already holds a snapshot where the value was absent. Use
/// <see cref="CreateScope"/> to explicitly propagate the user into background or parallel
/// work items.
/// </para>
/// <para>
/// <b>Instance field:</b> the <c>AsyncLocal&lt;T&gt;</c> is stored in a private instance
/// field. A <see langword="static"/> field is not required for correct propagation semantics
/// — <c>ExecutionContext</c> flows the value associated with each <c>AsyncLocal&lt;T&gt;</c>
/// instance regardless of static vs. instance placement. An instance field ensures each
/// DI-scoped accessor owns an independent slot, preventing cross-scope contamination.
/// </para>
/// </remarks>
public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private readonly AsyncLocal<ICurrentUser?> _current = new();

    /// <inheritdoc />
    public ICurrentUser? Get() => _current.Value;

    /// <inheritdoc />
    public void Set(ICurrentUser user) => _current.Value = user;

    /// <inheritdoc />
    public void Clear() => _current.Value = null;

    /// <inheritdoc />
    public IDisposable CreateScope(ICurrentUser user)
    {
        var previous = _current.Value;
        _current.Value = user;
        return new UserScope(_current, previous);
    }

    private sealed class UserScope(AsyncLocal<ICurrentUser?> local, ICurrentUser? previous)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            local.Value = previous;
            _disposed = true;
        }
    }
}
