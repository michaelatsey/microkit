namespace MicroKit.Messaging.Processing;

/// <summary>Whether an exception the store engine meets is the host stopping.</summary>
/// <remarks>
/// <para>
/// Stricter than the receive pipeline's rule on purpose: the engine treats an exception as a stop
/// only while the host's token is cancelled. The host cancels that token before it disposes
/// anything, so a stop always arrives that way — an <see cref="OperationCanceledException"/> or an
/// <see cref="ObjectDisposedException"/> from a scope or provider disposed under the work.
/// </para>
/// <para>
/// The same exceptions while the host keeps running are failures: a stop releases the claimed
/// rows with no attempt charged, so classifying a recurring fault as one would re-claim the same
/// row forever, and a loop that ended on one would stop draining its store while the host runs.
/// </para>
/// </remarks>
internal static class HostStop
{
    /// <summary>Whether <paramref name="exception"/> is the host stopping.</summary>
    /// <param name="exception">The exception met.</param>
    /// <param name="stoppingToken">The host's token.</param>
    /// <returns><see langword="true"/> for a stop; <see langword="false"/> for a failure.</returns>
    public static bool Is(Exception exception, CancellationToken stoppingToken)
        => stoppingToken.IsCancellationRequested
           && exception is OperationCanceledException or ObjectDisposedException;
}
