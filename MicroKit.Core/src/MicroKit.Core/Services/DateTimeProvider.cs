using MicroKit.Abstractions.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Core.Services;

public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTimeOffset UtcNowOffset => DateTimeOffset.UtcNow;
}
