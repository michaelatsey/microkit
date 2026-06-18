namespace MicroKit.Execution.Abstractions;

/// <summary>
/// Represents a resolved, contextualized execution scope for a single unit of work
/// (e.g. one outbox or inbox message). Wraps the underlying DI service scope and
/// any ambient context set by the <see cref="IExecutionScopeFactory"/> implementation.
/// </summary>
/// <remarks>
/// Always consumed with <see langword="await using"/> to guarantee deterministic disposal
/// of the inner DI scope and any ambient context that was pushed on scope creation.
/// </remarks>
public interface IExecutionScope : IAsyncDisposable
{
    /// <summary>
    /// The <see cref="IServiceProvider"/> for this scope. Use this to resolve scoped
    /// services (e.g. <c>IOutboxDispatcher</c>, <c>IMessageHandler&lt;T&gt;</c>) that
    /// must be fresh per message.
    /// </summary>
    IServiceProvider ServiceProvider { get; }
}
