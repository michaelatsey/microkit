namespace MicroKit.Persistence.Testing;

/// <summary>
/// The start-once, dispose-once lifecycle of one database container, shared by the fixtures of
/// MicroKit.Persistence.Testing.PostgreSql and .SqlServer (ADR-015). It knows nothing of Testcontainers: it
/// builds, starts and disposes a container through delegates.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The shared start runs under no caller's token, only under the lifecycle's own, which
/// <see cref="DisposeAsync"/> cancels. A caller's token cancels only that caller's wait.</item>
/// <item>A failed start is cached: every later <see cref="StartAsync"/> rethrows it. Once disposed, every call
/// throws <see cref="ObjectDisposedException"/>; a call already waiting when a dispose interrupts the start
/// receives the start's <see cref="OperationCanceledException"/>.</item>
/// <item>The start does not carry the first caller's ambient state (<see cref="ExecutionContext"/> flow is
/// suppressed): it belongs to no caller.</item>
/// <item><see cref="DisposeAsync"/> cancels a running start, waits for it, ignores its outcome and disposes
/// the container if one was built — a start can fail after the container exists. Every call, concurrent ones
/// included, completes once the container is removed.</item>
/// </list>
/// </remarks>
/// <typeparam name="TContainer">The container type.</typeparam>
internal sealed class ContainerLifecycle<TContainer> : IAsyncDisposable
    where TContainer : class, IAsyncDisposable
{
    private readonly Type _owner;
    private readonly Func<TContainer> _build;
    private readonly Func<TContainer, CancellationToken, Task> _start;
    private readonly CancellationTokenSource _disposing = new();
    private readonly Lock _gate = new();
    private Task? _starting;
    private Task? _disposal;
    private TContainer? _container;
    private bool _disposed;

    internal ContainerLifecycle(Type owner, Func<TContainer> build, Func<TContainer, CancellationToken, Task> start)
    {
        _owner = owner;
        _build = build;
        _start = start;
    }

    /// <summary>Gets the started container.</summary>
    /// <exception cref="ObjectDisposedException">The lifecycle is disposed.</exception>
    /// <exception cref="InvalidOperationException">No start has completed successfully.</exception>
    internal TContainer Running
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, _owner);
                if (_starting is not { IsCompletedSuccessfully: true })
                {
                    throw new InvalidOperationException(
                        $"The {_owner.Name} is not started: await StartAsync() first, and ask DockerProbe.IsAvailable " +
                        "before starting it, to skip the test when no Docker daemon answers.");
                }

                return _container!;
            }
        }
    }

    /// <summary>Starts the container once; every call awaits that one start. Faults come through the task.</summary>
    internal ValueTask StartAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return ValueTask.FromCanceled(ct);

        Task starting;
        lock (_gate)
        {
            if (_disposed)
                return ValueTask.FromException(new ObjectDisposedException(_owner.FullName));

            if (_starting is null)
            {
                using (ExecutionContext.SuppressFlow())
                    _starting = Task.Run(RunStartAsync, CancellationToken.None);
            }

            starting = _starting;
        }

        return new ValueTask(ct.CanBeCanceled ? starting.WaitAsync(ct) : starting);
    }

    /// <summary>
    /// Cancels a running start, waits for it, then disposes the container. Idempotent: every call returns the
    /// one disposal.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is null)
            {
                _disposed = true;
                var starting = _starting;
                _disposal = Task.Run(() => DisposeCoreAsync(starting), CancellationToken.None);
            }

            return new ValueTask(_disposal);
        }
    }

    private async Task DisposeCoreAsync(Task? starting)
    {
#pragma warning disable CA1031 // the start's outcome is cached for StartAsync; disposal only needs it finished
        try
        {
            await _disposing.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A cancellation callback of the start that throws must not leave the container behind.
        }

        if (starting is not null)
        {
            try
            {
                await starting.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A failed or interrupted start still leaves a container to dispose.
            }
        }
#pragma warning restore CA1031

        _disposing.Dispose();
        if (_container is not null)
            await _container.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunStartAsync()
    {
        var container = _build();
        _container = container;
        await _start(container, _disposing.Token).ConfigureAwait(false);
    }
}
