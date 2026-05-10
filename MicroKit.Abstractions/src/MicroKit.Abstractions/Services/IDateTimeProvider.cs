namespace MicroKit.Abstractions.Services;

/// <summary>
/// Abstraction for date/time operations to facilitate testing.
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Gets the current UTC date and time as DateTimeOffset.
    /// </summary>
    DateTimeOffset UtcNowOffset { get; }
}
