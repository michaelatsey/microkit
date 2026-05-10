using MediatR;
using MicroKit.Abstractions.Serialization;
using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Abstractions.Models;
using MicroKit.Idempotency.Core.Configuration;
using MicroKit.Idempotency.Core.Context;
using MicroKit.Idempotency.Core.Exceptions;
using MicroKit.Idempotency.Core.Hashing;
using MicroKit.MultiTenancy.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroKit.Idempotency.MediatR.Behaviors;

public class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IIdempotentRequest<TResponse>
{
    private readonly IIdempotencyStore _store;
    private readonly IMicroKitSerializer _serializer;
    private readonly RequestHasher _requestHasher;
    private readonly ILogger<IdempotencyBehavior<TRequest, TResponse>> _logger;
    private readonly IdempotencyOptions _options;
    private readonly IIdempotencyContext _idempotencyContext;
    private readonly IIdempotencyManager _manager;
    private readonly ITenantContext? _tenantContext;

    public IdempotencyBehavior(
        IIdempotencyStore store,
        IMicroKitSerializer serializer,
        IIdempotencyContext idempotencyContext,
        RequestHasher requestHasher,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotencyBehavior<TRequest, TResponse>> logger,
        IIdempotencyManager manager,
        ITenantContext? tenantContext = null
        )
    {
        _store = store;
        _serializer = serializer;
        _requestHasher = requestHasher;
        _logger = logger;
        _options = options.Value;
        _idempotencyContext = idempotencyContext;
        _manager = manager;
        _tenantContext = tenantContext;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IIdempotentRequest<TResponse> idemRequest)
        {
            // Ceci n'est pas un request idempotent, continuer normalement
            return await next(cancellationToken);
        }
        var key = request.IdempotencyKey;
        var tenantId = GetTenantId();

        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Idempotency key is null or empty");
            return await next(cancellationToken);
        }

        if(_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Received idempotent request with key: {Key}", key);
        }
        _manager.SetKey(key);

        using var scope = _idempotencyContext.BeginScope(key);

        var existingState = await _store.GetAsync(key, cancellationToken);

        if (existingState is not null)
        {
            (_idempotencyContext as IdempotencyContext)?.UpdateState(existingState);
            return await HandleExistingState(existingState, request, next, tenantId, cancellationToken);
        }

        return await ProcessNewRequest(key, tenantId, request, next, cancellationToken);
    }

    private string GetTenantId()
    {
        if (_tenantContext is null || _tenantContext?.Tenant is null)
        {
            return "single";
        }
        var tenantId = _tenantContext.Tenant.Id;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("Tenant ID is null or empty in tenant context");
            return "single";
        }
        return tenantId;
    }

    private async Task<TResponse> HandleExistingState(
        IdempotencyState existingState,
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_options.VerifyRequestHashes && existingState.RequestHash is not null)
        {
            var currentHash = _requestHasher.ComputeHash(request);
            if (existingState.RequestHash != currentHash)
            {
                throw new IdempotencyConflictException(
                    existingState.Key,
                    "Request content differs from original idempotent request");
            }
        }

        switch (existingState.Status)
        {
            case IdempotencyStatus.Processing:
                throw new IdempotencyProgressingException(existingState.Key);

            case IdempotencyStatus.Completed:
                if (_options.EnableLogging && _logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Returning cached response for key: {Key}", existingState.Key);

                if (string.IsNullOrEmpty(existingState.Response))
                {
                    _logger.LogWarning("Cached response is null or empty for key: {Key}", existingState.Key);
                    return default!;
                }
                return _serializer.Deserialize<TResponse>(existingState.Response) ?? default!;

            case IdempotencyStatus.Failed:
            case IdempotencyStatus.Cancelled:
                if (_options.EnableLogging && _logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Retrying {Status} operation for key: {Key}", existingState.Status, existingState.Key);

                await _store.DeleteAsync(existingState.Key, cancellationToken);
                return await ProcessNewRequest(existingState.Key, tenantId, request, next, cancellationToken);

            default:
                throw new InvalidOperationException($"Unexpected idempotency status: {existingState.Status}");
        }
    }

    private async Task<TResponse> ProcessNewRequest(
        string key,
        string tenantId,
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var ttl = request.IdempotencyExpiration ?? _options.DefaultExpiration;
        var requestHash = _options.VerifyRequestHashes ? _requestHasher.ComputeHash(request) : null;
        // TODO: Consider tenant key if multi-tenancy is supported
        var initialState = new IdempotencyState(key, tenantId, IdempotencyStatus.Processing)
        {
            RequestHash = requestHash
        };

        try
        {
            await _store.CreateAsync(initialState, ttl, cancellationToken);

            var response = await next(cancellationToken);

            var serializedResponse = _serializer.Serialize(response);

            await _store.CompleteAsync(
                key,
                serializedResponse,
                IdempotencyStatus.Completed,
                cancellationToken);

            return response;
        }
        catch (Exception ex) when (ex is not IdempotencyProgressingException)
        {
            var errorStatus = DetermineErrorStatus(ex);
            await _store.FailAsync(key, errorStatus, cancellationToken);
            throw;
        }
    }

    private static IdempotencyStatus DetermineErrorStatus(Exception ex) =>
        ex is OperationCanceledException ? IdempotencyStatus.Cancelled : IdempotencyStatus.Failed;
}


