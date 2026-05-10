using MicroKit.Abstractions.Clock;

namespace MicroKit.Core.Times;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
