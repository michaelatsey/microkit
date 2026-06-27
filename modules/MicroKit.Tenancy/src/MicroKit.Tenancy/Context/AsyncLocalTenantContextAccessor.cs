namespace MicroKit.Tenancy;

/// <summary>
/// Host-agnostic <see cref="ITenantContextAccessor"/> backed by <see cref="AsyncLocal{T}"/>.
/// Works in any async execution context: HTTP, queue consumers, background jobs, gRPC.
/// Must be registered as <b>Scoped</b> — never Singleton (enforced by MKT003 analyzer).
/// </summary>
public sealed class AsyncLocalTenantContextAccessor : ITenantContextAccessor
{
    private readonly AsyncLocal<ITenantInfo?> _current = new();

    /// <inheritdoc/>
    public ITenantInfo? CurrentTenant => _current.Value;

    /// <inheritdoc/>
    public void SetTenant(ITenantInfo? tenant) => _current.Value = tenant;

    /// <inheritdoc/>
    public IDisposable CreateScope(ITenantInfo tenant)
    {
        var previous = _current.Value;
        _current.Value = tenant;
        return new TenantScope(_current, previous);
    }

    private sealed class TenantScope(AsyncLocal<ITenantInfo?> current, ITenantInfo? previous)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            current.Value = previous;
            _disposed = true;
        }
    }
}
