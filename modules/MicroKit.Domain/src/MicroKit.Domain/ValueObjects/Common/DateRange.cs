using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Represents a date range with start and end boundaries.
/// Provides operations for checking containment, overlaps, and calculating duration.
/// </summary>
/// <param name="Start">The start date and time of the range</param>
/// <param name="End">The end date and time of the range</param>
public sealed record DateRange(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>
    /// Gets the start date and time of the range.
    /// </summary>
    public DateTimeOffset Start { get; } = Start;

    /// <summary>
    /// Gets the end date and time of the range.
    /// </summary>
    public DateTimeOffset End { get; } = ValidateRange(Start, End);

    /// <summary>
    /// Gets the duration of this date range.
    /// </summary>
    public TimeSpan Duration => End - Start;

    /// <summary>
    /// Validates that the start date is not after the end date.
    /// </summary>
    /// <param name="start">The start date</param>
    /// <param name="end">The end date</param>
    /// <returns>The validated end date</returns>
    /// <exception cref="DomainException">Thrown when start is after end</exception>
    private static DateTimeOffset ValidateRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (start > end)
            throw new DomainException("Start date cannot be after end date.");

        return end;
    }

    /// <summary>
    /// Determines whether the specified date falls within this range (inclusive).
    /// </summary>
    /// <param name="date">The date to check</param>
    /// <returns>True if the date is within the range; otherwise, false</returns>
    public bool Contains(DateTimeOffset date)
    {
        return date >= Start && date <= End;
    }

    /// <summary>
    /// Determines whether this date range overlaps with another date range.
    /// </summary>
    /// <param name="other">The other date range to check for overlap</param>
    /// <returns>True if the ranges overlap; otherwise, false</returns>
    /// <exception cref="ArgumentNullException">Thrown when other is null</exception>
    public bool Overlaps(DateRange other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Start <= other.End && End >= other.Start;
    }

    /// <summary>
    /// Creates a new DateRange instance.
    /// </summary>
    /// <param name="start">The start date and time</param>
    /// <param name="end">The end date and time</param>
    /// <returns>A new DateRange instance</returns>
    /// <exception cref="DomainException">Thrown when start is after end</exception>
    public static DateRange Create(DateTimeOffset start, DateTimeOffset end)
    {
        return new DateRange(start, end);
    }

    /// <summary>
    /// Creates a date range for a single day.
    /// </summary>
    /// <param name="date">The date</param>
    /// <returns>A DateRange covering the entire day</returns>
    public static DateRange ForDay(DateTimeOffset date)
    {
        var dayStart = new DateTimeOffset(date.Date, date.Offset);
        var dayEnd = dayStart.AddDays(1).AddTicks(-1);
        return new DateRange(dayStart, dayEnd);
    }

    /// <summary>
    /// Creates a date range from a start date with a specific duration.
    /// </summary>
    /// <param name="start">The start date and time</param>
    /// <param name="duration">The duration of the range</param>
    /// <returns>A new DateRange instance</returns>
    /// <exception cref="DomainException">Thrown when duration is negative</exception>
    public static DateRange FromDuration(DateTimeOffset start, TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new DomainException("Duration cannot be negative.");

        return new DateRange(start, start.Add(duration));
    }

    /// <summary>
    /// Returns a string representation of this date range.
    /// </summary>
    /// <returns>A formatted string showing the start and end dates</returns>
    public override string ToString()
    {
        return $"{Start:yyyy-MM-dd HH:mm:ss zzz} - {End:yyyy-MM-dd HH:mm:ss zzz}";
    }
}