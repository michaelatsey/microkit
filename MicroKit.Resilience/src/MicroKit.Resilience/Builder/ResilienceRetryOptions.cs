using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Resilience.Builder;

public class ResilienceRetryOptions
{
    public int RetryCount { get; set; } = 3;
    public string PipelineName { get; set; } = "DefaultRetry";
    public double BaseDelaySeconds { get; set; } = 1.0;
    public bool EnableFallback { get; set; } = true;

    // --- Options du Circuit Breaker ---
    public bool EnableCircuitBreaker { get; set; } = true;
    public double FailureRatio { get; set; } = 0.5; // 50% d'échecs
    public int MinimumThroughput { get; set; } = 10; // Minimum 10 appels
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30); // ...on coupe pendant 30s
}
