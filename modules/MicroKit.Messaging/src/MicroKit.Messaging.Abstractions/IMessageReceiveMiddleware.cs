namespace MicroKit.Messaging;

/// <summary>
/// A step of the receive pipeline, run in a message's scope before its handler.
/// </summary>
/// <remarks>
/// <para>
/// Middleware runs in registration order, once per handler, after the scope initializers and
/// before the handler is resolved. Each step calls <c>proceed</c> to continue; not calling it skips
/// the handler, and the delivery then counts as handled for that handler.
/// </para>
/// <para>
/// An exception is classified like a handler's: <see cref="PermanentMessageException"/>
/// dead-letters, <see cref="InboxDependencyUnavailableException"/> pauses the endpoint, anything
/// else redelivers.
/// </para>
/// </remarks>
public interface IMessageReceiveMiddleware
{
    /// <summary>Runs this step.</summary>
    /// <param name="context">The delivery in flight.</param>
    /// <param name="proceed">The rest of the pipeline, ending with the handler.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when this step and the rest are done.</returns>
    ValueTask InvokeAsync(
        MessageReceiveContext context,
        Func<ValueTask> proceed,
        CancellationToken ct = default);
}
