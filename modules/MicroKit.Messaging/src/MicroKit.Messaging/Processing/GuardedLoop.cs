namespace MicroKit.Messaging.Processing;

/// <summary>
/// The one loop guard every long-running loop of the module runs under — the store engine's four
/// loops per store, the in-memory transport's consume loop and the local queue's (#188): only a
/// host stop — or, for a loop given a removal token, that token (#191) — ends a loop, and nothing it
/// logs can end it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shutdown is <see cref="HostStop"/>, nothing else.</b> An exception ends the loop only while the
/// host's token is cancelled. Any other — an <see cref="ObjectDisposedException"/> while the host
/// runs included — is a failed iteration: reported, delayed by the loop's own rule, and the loop
/// goes on.
/// </para>
/// <para>
/// <b>A removal is read between iterations, never inside one.</b> A loop given a removal token ends
/// before its next iteration, or during its delay, once that token is cancelled; the iteration in
/// flight never sees it, so a batch it holds runs to its settlement.
/// </para>
/// <para>
/// <b>Logging cannot throw.</b> Every log call of a loop goes through <see cref="Log"/>, which swallows
/// a logging provider's fault: there is nowhere left to report it, and it must not end the loop or
/// undo a settlement.
/// </para>
/// </remarks>
internal static class GuardedLoop
{
    /// <summary>
    /// Runs <paramref name="iteration"/> until the host stops, <paramref name="removal"/> is cancelled,
    /// or an iteration asks to stop.
    /// </summary>
    /// <param name="iteration">
    /// One pass: returns the delay before the next one (<see cref="TimeSpan.Zero"/> for none), or
    /// <see langword="null"/> to end the loop for a reason of its own (a configuration fault).
    /// </param>
    /// <param name="onFault">
    /// Reports a failed iteration — through <see cref="Log"/>, so it cannot throw — and returns the
    /// delay before the next.
    /// </param>
    /// <param name="timeProvider">The clock the delays run on.</param>
    /// <param name="stoppingToken">The host's token.</param>
    /// <param name="removal">
    /// Asks the loop to end without stopping the host (#191: its store instance was removed). It is
    /// read between iterations and cuts a delay short, and never reaches <paramref name="iteration"/>:
    /// an iteration in flight — a batch — runs to its settlement on the host's token alone.
    /// </param>
    /// <returns>A task that completes when the loop ends.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Only a host stop may end a loop: every other fault is a failed iteration.")]
    public static async Task RunAsync(
        Func<CancellationToken, ValueTask<TimeSpan?>> iteration,
        Func<Exception, TimeSpan> onFault,
        TimeProvider timeProvider,
        CancellationToken stoppingToken,
        CancellationToken removal = default)
    {
        // One linked source for the whole loop, and only for a loop that can be removed: a single
        // store's loop delays on the host's token, as it always has.
        using var linked = removal.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, removal)
            : null;
        var delayToken = linked?.Token ?? stoppingToken;

        while (!stoppingToken.IsCancellationRequested && !removal.IsCancellationRequested)
        {
            TimeSpan? next;
            try
            {
                next = await iteration(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (HostStop.Is(ex, stoppingToken))
            {
                return;
            }
            catch (Exception ex)
            {
                next = onFault(ex);
            }

            if (next is not { } delay)
            {
                return;
            }

            if (delay <= TimeSpan.Zero)
            {
                continue;
            }

            try
            {
                await Task.Delay(delay, timeProvider, delayToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || removal.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>Runs one log call; a logging provider's fault is swallowed, never the loop's end.</summary>
    /// <param name="log">The log call.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A logging provider's fault must not end a loop or undo a settlement.")]
    public static void Log(Action log)
    {
        try
        {
            log();
        }
        catch (Exception)
        {
            // Deliberately swallowed: see the summary.
        }
    }
}
