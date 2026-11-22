namespace MicroKit.Messaging.Processing;

/// <summary>
/// The inbox retry policy, shared by a failed delivery (its first attempt) and a store's retry loop
/// (every later one): how many attempts a handler gets, and when the next one runs.
/// </summary>
/// <remarks>
/// Full jitter over an exponential ceiling — <c>Uniform(0, min(2^attempts s, MaxRetryBackoff))</c>.
/// Without the jitter, the rows that fail together, which is exactly what an outage produces, retry
/// together on every later attempt. The clock and the jitter source are injected, so the curve is
/// asserted against exact values.
/// </remarks>
internal sealed class InboxRetryPolicy(InboxProcessorOptions options, TimeProvider timeProvider, Random random)
{
    /// <summary>Gets the number of attempts after which a failing handler is dead-lettered.</summary>
    public int MaxRetries => options.MaxRetries;

    /// <summary>Gets the clock the deadlines are computed from.</summary>
    public TimeProvider Clock => timeProvider;

    /// <summary>Whether a handler that has now failed <paramref name="attempts"/> times is out of attempts.</summary>
    /// <param name="attempts">The failed attempts, this one included.</param>
    /// <returns><see langword="true"/> when the row is dead-lettered.</returns>
    public bool IsExhausted(int attempts) => attempts >= options.MaxRetries;

    /// <summary>When the next attempt runs, after <paramref name="attempts"/> failed ones.</summary>
    /// <param name="attempts">The failed attempts, this one included.</param>
    /// <returns>The earliest time of the next attempt.</returns>
    public DateTimeOffset NextAttemptAt(int attempts)
        => timeProvider.GetUtcNow().Add(ApplyJitter(ComputeBackoffCeiling(attempts)));

    /// <summary>
    /// The deterministic half of the back-off: <c>2^attempts</c> seconds, capped at
    /// <see cref="InboxProcessorOptions.MaxRetryBackoff"/>. The exponent is clamped, so a corrupt
    /// count cannot overflow.
    /// </summary>
    /// <param name="attempts">The failed attempts.</param>
    /// <returns>The ceiling of the delay.</returns>
    public TimeSpan ComputeBackoffCeiling(int attempts)
    {
        var exponent = Math.Clamp(attempts, 0, 20);
        var backoff = TimeSpan.FromSeconds(1L << exponent);

        return backoff > options.MaxRetryBackoff ? options.MaxRetryBackoff : backoff;
    }

    /// <summary>Applies full jitter: a uniform draw over <c>[0, ceiling]</c>.</summary>
    /// <param name="ceiling">The ceiling.</param>
    /// <returns>The delay.</returns>
    public TimeSpan ApplyJitter(TimeSpan ceiling) =>
        ceiling <= TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromTicks((long)(ceiling.Ticks * random.NextDouble()));

    /// <summary>Truncates an error text to the column's length, never splitting a surrogate pair.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The text, at most <see cref="InboxProcessorOptions.MaxErrorMessageLength"/> long.</returns>
    public string? Truncate(string? value) => ErrorText.Cut(value, options.MaxErrorMessageLength);
}
