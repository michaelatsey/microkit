namespace MicroKit.Logging;

/// <summary>
/// Provides access to the ambient <see cref="IOperationContext"/> for the current async execution scope.
/// </summary>
/// <remarks>
/// <para>
/// Accessing <see cref="Current"/> must be allocation-free (backed by <c>AsyncLocal&lt;T&gt;</c> field reads).
/// </para>
/// <para>
/// Register the implementation via the Core DI builder. Consume this interface in any MicroKit module
/// that needs to read the current correlation or identity context without depending on Core.
/// </para>
/// </remarks>
public interface ILogContextAccessor
{
    /// <summary>
    /// Gets the current operation context, or <see langword="null"/> if no scope is active
    /// (i.e., <see cref="ILogScopeFactory.BeginOperationScope()"/> has not been called in this async flow).
    /// </summary>
    IOperationContext? Current { get; }
}
