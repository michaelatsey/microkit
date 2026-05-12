using MicroKit.Abstractions.Clock;

namespace MicroKit.Core.Times;

/// <summary>Default <see cref="IClock"/> implementation backed by the system clock.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
