namespace MicroKit.Logging.Internal;

/// <summary>
/// Ambient operation context propagation across async boundaries.
/// Flows with ExecutionContext (async/await, Task.Run, thread-pool work items).
/// Values are replaced atomically — never mutated in place.
/// Maximum nesting: 3 levels before a documented justification is required.
/// </summary>
internal sealed class LogContextAccessor : ILogContextAccessor
{
    // AsyncLocal purpose: carry IOperationContext across async boundaries for the current operation.
    // Propagation scope: flows into child tasks; does NOT flow back to parent on completion.
    private static readonly AsyncLocal<OperationContext?> s_current = new();

    internal static OperationContext? CurrentContext
    {
        get => s_current.Value;
        set => s_current.Value = value;
    }

    IOperationContext? ILogContextAccessor.Current => s_current.Value;
}
