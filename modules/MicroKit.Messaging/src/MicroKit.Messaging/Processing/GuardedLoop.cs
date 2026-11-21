namespace MicroKit.Messaging.Processing;

/// <summary>
/// The one loop guard every long-running loop of the module runs under — the store engine's four
/// loops per store, the in-memory transport's consume loop and the local queue's (#188): only a
/// host stop ends a loop, and nothing it logs can end it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shutdown is <see cref="HostStop"/>, nothing else.</b> An exception ends the loop only while the
/// host's token is cancelled. Any other — an <see cref="ObjectDisposedException"/> while the host
/// runs included — is a failed iteration: reported, delayed by the loop's own rule, and the loop
/// goes on.
/// </para>
/// <para>
/// <b>Logging cannot throw.</b> Every log call of a loop goes through <see cref="Log"/>, which swallows
/// a logging provider's fault: there is nowhere left to report it, and it must not end the loop or
/// undo a settlement.
/// </para>
/// </remarks>
internal static class GuardedLoop
{
    /// <summary>Runs <paramref name="iteration"/> until the host stops or an iteration asks to stop.</summary>
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
    /// <returns>A task that completes when the loop ends.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Only a host stop may end a loop: every other fault is a failed iteration.")]
    public static async Task RunAsync(
        Func<CancellationToken, ValueTask<TimeSpan?>> iteration,
        Func<Exception, TimeSpan> onFault,
        TimeProvider timeProvider,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
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
                await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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
