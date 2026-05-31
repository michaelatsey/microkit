namespace MicroKit.MediatR.Behaviors;

/// <summary>
/// Unconditionally logs every dispatched request with its duration and outcome.
/// Emits an OpenTelemetry <see cref="Activity"/> tagged with the request name
/// when a listener is attached.
/// Pipeline order: <see cref="PipelineOrder.Logging"/> (100). Never short-circuits.
/// </summary>
/// <remarks>
/// All log delegates use <c>[LoggerMessage]</c> source generation — zero allocation,
/// zero boxing. <c>ActivitySource.HasListeners()</c> is checked before creating a span
/// so no tracing overhead occurs when no OTEL listener is configured.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed partial class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : BehaviorBase<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly string _requestName = typeof(TRequest).Name;
    private static readonly ActivitySource _activitySource = new("MicroKit.MediatR", "1.0.0");

    /// <inheritdoc />
    public override int Order => PipelineOrder.Logging;

    /// <inheritdoc />
    public override async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var startTimestamp = Stopwatch.GetTimestamp();

        using var activity = _activitySource.HasListeners()
            ? _activitySource.StartActivity(_requestName)
            : null;

        activity?.SetTag(LogPropertyNames.CommandName, _requestName);

        var operationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            [LogPropertyNames.CommandName] = _requestName,
            [LogPropertyNames.OperationId] = operationId,
        });

        LogHandlingStarted(logger, _requestName);

        try
        {
            var response = await next().ConfigureAwait(false);
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Result.Failure is a business failure — log at Warning and mark the span as Error
            // so observability tooling can distinguish domain failures from true successes.
            if (MicroKit.MediatR.Behaviors.Pipeline.ResultInspector<TResponse>.IsFailure(response))
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                LogHandlingBusinessFailure(logger, _requestName, elapsedMs);
            }
            else
            {
                LogHandlingSucceeded(logger, _requestName, elapsedMs);
            }

            return response;
        }
        catch (Exception ex)
        {
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            LogHandlingFailed(logger, ex, _requestName, elapsedMs);
            throw;
        }
    }

    [LoggerMessage(1000, LogLevel.Information, "Handling {CommandName}")]
    private static partial void LogHandlingStarted(ILogger logger, string commandName);

    [LoggerMessage(1001, LogLevel.Information, "Handled {CommandName} in {ElapsedMs}ms")]
    private static partial void LogHandlingSucceeded(ILogger logger, string commandName, long elapsedMs);

    [LoggerMessage(1002, LogLevel.Warning, "Failed {CommandName} after {ElapsedMs}ms")]
    private static partial void LogHandlingFailed(ILogger logger, Exception ex, string commandName, long elapsedMs);

    [LoggerMessage(1003, LogLevel.Warning, "BusinessFailure {CommandName} in {ElapsedMs}ms")]
    private static partial void LogHandlingBusinessFailure(ILogger logger, string commandName, long elapsedMs);
}
