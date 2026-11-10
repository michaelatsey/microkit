namespace MicroKit.Messaging.Bus;

/// <summary>
/// What a transport must do with one delivery. Ordered from least to most redeliverable, so the
/// verdict of a message with several handlers is the maximum of theirs.
/// </summary>
internal enum DeliveryVerdict
{
    /// <summary>Every handler succeeded, or none is registered: acknowledge.</summary>
    Succeeded = 0,

    /// <summary>The message can never succeed: dead-letter it at once.</summary>
    Permanent = 1,

    /// <summary>The attempt failed: redeliver, counting an attempt.</summary>
    Transient = 2,

    /// <summary>A shared dependency is down: pause the endpoint, redeliver without counting.</summary>
    DependencyUnavailable = 3,

    /// <summary>
    /// The host is shutting down: leave the message unsettled, never classified, attempt count
    /// unchanged.
    /// </summary>
    Unsettled = 4,
}

/// <summary>The verdict of one delivery, with the fault that decided it.</summary>
/// <param name="Verdict">What the transport must do.</param>
/// <param name="Error">The exception behind a failed verdict, if any.</param>
/// <param name="Reason">A human-readable reason for a failed verdict.</param>
internal readonly record struct DeliveryOutcome(
    DeliveryVerdict Verdict, Exception? Error = null, string? Reason = null)
{
    /// <summary>The successful outcome.</summary>
    public static DeliveryOutcome Success { get; } = new(DeliveryVerdict.Succeeded);

    /// <summary>A permanent outcome.</summary>
    /// <param name="reason">Why the message can never succeed.</param>
    /// <param name="error">The underlying fault, if any.</param>
    /// <returns>The outcome.</returns>
    public static DeliveryOutcome Permanent(string reason, Exception? error = null)
        => new(DeliveryVerdict.Permanent, error, reason);

    /// <summary>The more redeliverable of two outcomes; the first wins a tie.</summary>
    /// <param name="left">One outcome.</param>
    /// <param name="right">The other.</param>
    /// <returns>The outcome with the higher verdict.</returns>
    public static DeliveryOutcome MostRedeliverable(DeliveryOutcome left, DeliveryOutcome right)
        => right.Verdict > left.Verdict ? right : left;
}
