namespace MicroKit.Messaging.Processing;

/// <summary>
/// The one curve of every outage back-off the engine keeps — an outbox endpoint's, a tenant's on an
/// endpoint, a tenant's on a store's inbox (#188, #124) — so the endpoint and the tenant curves cannot
/// drift apart.
/// </summary>
internal static class BackoffCurve
{
    /// <summary>The delay of a new failure, from the previous back-off, if any.</summary>
    /// <param name="previousUntil">When the previous back-off ended, or <see langword="null"/> for none.</param>
    /// <param name="previousDelay">The previous back-off's delay.</param>
    /// <param name="now">The time of the failure.</param>
    /// <param name="initial">The base delay.</param>
    /// <param name="ceiling">The largest delay.</param>
    /// <returns>The delay to wait from <paramref name="now"/>.</returns>
    /// <remarks>
    /// Doubles from the last failure — unless a whole ceiling went by past the end of the back-off
    /// without failing again: a new outage then starts from the base delay. The ceiling, not the last
    /// delay: the loop's own idle polling stretches the gap before the next try.
    /// </remarks>
    public static TimeSpan Next(
        DateTimeOffset? previousUntil, TimeSpan previousDelay, DateTimeOffset now, TimeSpan initial, TimeSpan ceiling)
    {
        var delay = previousUntil is not { } until || now >= until + ceiling ? initial : previousDelay * 2;
        return delay > ceiling ? ceiling : delay;
    }
}
