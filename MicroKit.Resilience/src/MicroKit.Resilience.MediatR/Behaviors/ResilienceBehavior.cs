using MediatR;
using MicroKit.Resilience.Abstractions;
using MicroKit.Resilience.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;

namespace MicroKit.Resilience.MediatR.Behaviors;

/// <summary>
/// MediatR pipeline behavior that wraps command/query execution with Polly resilience policies.
/// </summary>
/// <remarks>
/// This behavior intercepts request handling and executes them within a Polly resilience pipeline.
/// If a request implements <see cref="IResilientRequest"/>, its specified pipeline is used;
/// otherwise, the default pipeline from <see cref="ResilienceRetryOptions"/> is applied.
/// </remarks>
/// <typeparam name="TRequest">The type of request being handled.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the handler.</typeparam>
public sealed class ResilienceBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<ResilienceBehavior<TRequest, TResponse>> _logger;
    private readonly ResiliencePipelineProvider<string> _pipelineProvider;
    private readonly ResilienceRetryOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResilienceBehavior{TRequest,TResponse}"/> class.
    /// </summary>
    /// <param name="logger">The logger for behavior diagnostics.</param>
    /// <param name="pipelineProvider">The Polly pipeline provider from the registry.</param>
    /// <param name="options">The resilience retry options from configuration.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when logger, pipelineProvider, or options is null.
    /// </exception>
    public ResilienceBehavior(
        ILogger<ResilienceBehavior<TRequest, TResponse>> logger,
        ResiliencePipelineProvider<string> pipelineProvider,
        IOptions<ResilienceRetryOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipelineProvider = pipelineProvider ?? throw new ArgumentNullException(nameof(pipelineProvider));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    }

    /// <summary>
    /// Handles the incoming request by executing it within a resilience pipeline.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="next">The next behavior in the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response from the handler.</returns>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Determine which pipeline to use
        var pipelineName = (request is IResilientRequest resilientRequest && !string.IsNullOrEmpty(resilientRequest.PipelineName))
            ? resilientRequest.PipelineName
            : _options.PipelineName;

        // Retrieve pipeline from registry
        var pipeline = _pipelineProvider.GetPipeline(pipelineName);

        // Execute within the resilience pipeline
        return await pipeline.ExecuteAsync(async ct => await next(ct), cancellationToken);
    }
}
