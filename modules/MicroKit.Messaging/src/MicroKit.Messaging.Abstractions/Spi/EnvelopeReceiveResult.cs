namespace MicroKit.Messaging.Spi;

/// <summary>
/// The answer of <see cref="IEnvelopeReceiver.ReceiveAsync"/>: what the transport must do with the
/// message, and why.
/// </summary>
/// <remarks>
/// <para>
/// Part of the transport SPI (<c>MicroKit.Messaging.Spi</c>): Core builds it, a broker provider
/// reads it.
/// </para>
/// <para>
/// A class with a private constructor and static factories rather than a positional record: it
/// travels outward, so it can gain a member without breaking a provider's call site.
/// </para>
/// </remarks>
public sealed record EnvelopeReceiveResult
{
    private EnvelopeReceiveResult(ReceiveDisposition disposition, string? reason, Exception? error)
    {
        Disposition = disposition;
        Reason = reason;
        Error = error;
    }

    /// <summary>Gets an <see cref="ReceiveDisposition.Acknowledge"/> result.</summary>
    public static EnvelopeReceiveResult Acknowledged { get; } = new(ReceiveDisposition.Acknowledge, null, null);

    /// <summary>Gets what the transport must do with the message.</summary>
    public ReceiveDisposition Disposition { get; }

    /// <summary>
    /// Gets why the message is not acknowledged, for the transport's logs and dead-letter metadata.
    /// <see langword="null"/> on <see cref="ReceiveDisposition.Acknowledge"/>.
    /// </summary>
    public string? Reason { get; }

    /// <summary>Gets the exception behind the disposition, when there is one.</summary>
    public Exception? Error { get; }

    /// <summary>Creates a result.</summary>
    /// <param name="disposition">What the transport must do.</param>
    /// <param name="reason">Why, for any disposition but <see cref="ReceiveDisposition.Acknowledge"/>.</param>
    /// <param name="error">The exception behind it, if any.</param>
    /// <returns>The result.</returns>
    public static EnvelopeReceiveResult From(
        ReceiveDisposition disposition, string? reason = null, Exception? error = null)
        => disposition is ReceiveDisposition.Acknowledge && reason is null && error is null
            ? Acknowledged
            : new(disposition, reason, error);
}
