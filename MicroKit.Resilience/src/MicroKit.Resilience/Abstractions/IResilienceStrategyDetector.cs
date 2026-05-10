using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Resilience.Abstractions;

public interface IResilienceStrategyDetector : IResilienceErrorDetector
{
    bool CanHandle(Exception ex);
}
