namespace MicroKit.Auth.Testing.Fakes;

/// <summary>
/// Simple field-backed <see cref="ICurrentUserAccessor"/> test double.
/// Unlike <c>CurrentUserAccessor</c>, this does not use <see cref="System.Threading.AsyncLocal{T}"/>;
/// state is held in an instance field, making it predictable in unit test assertions.
/// </summary>
public sealed class FakeCurrentUserAccessor : ICurrentUserAccessor
{
    private ICurrentUser? _current;

    /// <inheritdoc />
    public ICurrentUser? Get() => _current;

    /// <inheritdoc />
    public void Set(ICurrentUser user) => _current = user;

    /// <inheritdoc />
    public void Clear() => _current = null;

    /// <inheritdoc />
    public IDisposable CreateScope(ICurrentUser user)
    {
        var previous = _current;
        _current = user;
        return new UserScope(this, previous);
    }

    private sealed class UserScope(FakeCurrentUserAccessor accessor, ICurrentUser? previous)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            accessor._current = previous;
            _disposed = true;
        }
    }
}
