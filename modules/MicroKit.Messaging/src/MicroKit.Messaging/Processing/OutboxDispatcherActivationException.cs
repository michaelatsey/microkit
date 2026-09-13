namespace MicroKit.Messaging.Processing;

/// <summary>
/// Marks an exception thrown while the container was <b>activating</b> a registered
/// <see cref="IOutboxDispatcher"/>, so <see cref="OutboxProcessor"/> can classify it by type.
/// </summary>
/// <remarks>
/// <para>
/// Raised in exactly one place — around the <c>GetService</c> call in
/// <c>OutboxProcessor.ResolveDispatcher</c> — and caught in exactly one place, the batch loop, which
/// abandons the batch without consuming retry budget and without rethrowing. It never escapes
/// <see cref="OutboxProcessor.ProcessBatchAsync"/>.
/// </para>
/// <para>
/// <b>A dedicated type, because the alternative is an exception inspection.</b> What it wraps is an
/// <see cref="InvalidOperationException"/> — the type the container throws for a dependency it cannot
/// supply, and the only type <c>ResolveDispatcher</c> tags — and a dispatcher's own
/// <c>DispatchAsync</c> may throw that type too, as a genuine per-message fault. Catching it by type
/// in the loop would swallow both. Wrapping it at the only call that can produce an activation failure
/// keeps the classification structural: the loop matches a type nothing else throws. Any other type
/// thrown during activation is not wrapped, and keeps its own classification.
/// </para>
/// <para>
/// Derives from <see cref="Exception"/>, deliberately not from <see cref="InvalidOperationException"/>,
/// so no <c>catch (InvalidOperationException)</c> anywhere can capture it by accident.
/// </para>
/// </remarks>
internal sealed class OutboxDispatcherActivationException : Exception
{
    /// <summary>Initializes a new <see cref="OutboxDispatcherActivationException"/>.</summary>
    /// <param name="innerException">What the container threw while activating the dispatcher.</param>
    public OutboxDispatcherActivationException(Exception innerException)
        : base(
            $"{nameof(IOutboxDispatcher)} is registered but could not be activated. The inner " +
            "exception is what the container threw.",
            innerException)
    {
    }
}
