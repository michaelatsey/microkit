using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Abstractions.Clock;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
