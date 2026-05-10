using MediatR;
using MicroKit.Cqrs.Abstractions.Resilients;
using MicroKit.Resilience.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;

namespace MicroKit.Resilience.MediatR.Behaviors;

public class ResilienceBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<ResilienceBehavior<TRequest, TResponse>> _logger;
    private readonly ResiliencePipelineProvider<string> _pipelineProvider;
    private readonly ResilienceRetryOptions _options;
    public ResilienceBehavior(
        ILogger<ResilienceBehavior<TRequest, TResponse>> logger, 
        ResiliencePipelineProvider<string> pipelineProvider,
        IOptions<ResilienceRetryOptions> options)
    {
        _logger = logger;
        _pipelineProvider = pipelineProvider;
        _options = options.Value;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // 1. Détermination du nom du pipeline
        // Si la requête implémente IResilientRequest, on prend son nom, 
        // sinon on utilise le nom par défaut configuré dans les options.
        var pipelineName = (request is IResilientRequest resilientRequest && !string.IsNullOrEmpty(resilientRequest.PipelineName))
            ? resilientRequest.PipelineName
            : _options.PipelineName ; // Fallback sur "DefaultRetry"

        // 2. Récupération du pipeline depuis le registre centralisé de Polly
        var pipeline = _pipelineProvider.GetPipeline(pipelineName);

        // 3. Exécution sécurisée
        return await pipeline.ExecuteAsync(async ct => await next(ct), cancellationToken);


    }
}
