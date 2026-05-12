using MicroKit.Abstractions.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Core.Services;

/// <summary>Default <see cref="IDateTimeProvider"/> implementation backed by the system clock.</summary>
public sealed class DateTimeProvider : IDateTimeProvider
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;

    /// <inheritdoc />
    public DateTimeOffset UtcNowOffset => DateTimeOffset.UtcNow;
}
