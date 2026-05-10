using MicroKit.Resilience.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Resilience;

public class CompositeResilienceDetector : IResilienceErrorDetector
{
    private readonly IEnumerable<IResilienceStrategyDetector> _detectors;

    public CompositeResilienceDetector(IEnumerable<IResilienceStrategyDetector> detectors)
    {
        _detectors = detectors;
    }

    public bool ShouldRetry(Exception ex) 
        => _detectors.Any(d => d.CanHandle(ex) && d.ShouldRetry(ex));
}
