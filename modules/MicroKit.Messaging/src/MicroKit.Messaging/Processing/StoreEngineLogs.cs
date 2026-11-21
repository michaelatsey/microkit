namespace MicroKit.Messaging.Processing;

/// <summary>Logs of the per-store engine: the loops, not what they process.</summary>
internal static partial class StoreEngineLogs
{
    [LoggerMessage(
        EventId = 1400,
        Level = LogLevel.Error,
        Message = "The {Loop} loop of store '{Store}' could not activate {Service}: it is registered, " +
                  "and its construction threw. Retrying after {Delay}. Nothing was claimed.")]
    public static partial void ActivationFailed(
        ILogger logger, Exception exception, string loop, string store, string service, TimeSpan delay);

    [LoggerMessage(
        EventId = 1401,
        Level = LogLevel.Error,
        Message = "The {Loop} loop of store '{Store}' failed; retrying after {Delay}. No other store " +
                  "is affected.")]
    public static partial void LoopFailed(
        ILogger logger, Exception exception, string loop, string store, TimeSpan delay);

    [LoggerMessage(
        EventId = 1402,
        Level = LogLevel.Critical,
        Message = "The {Loop} loop of store '{Store}' met a configuration fault; every claimed row was " +
                  "released, and it retries after {Delay}. It cannot clear without a fix: correct the " +
                  "composition and redeploy.")]
    public static partial void ConfigurationFault(
        ILogger logger, Exception exception, string loop, string store, TimeSpan delay);

    [LoggerMessage(
        EventId = 1403,
        Level = LogLevel.Information,
        Message = "Draining {Count} message store(s): {Stores}.")]
    public static partial void EngineStarted(ILogger logger, int count, string stores);
}
