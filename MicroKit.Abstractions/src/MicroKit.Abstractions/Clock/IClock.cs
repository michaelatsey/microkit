using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Abstractions.Clock;

/// <summary>Abstraction for retrieving the current UTC time, enabling deterministic testing.</summary>
public interface IClock
{
    /// <summary>Gets the current UTC date and time.</summary>
    DateTimeOffset UtcNow { get; }
}
