//using MediatR;

//namespace MicroKit.Cqrs.MediatR.Behaviors;

//public sealed class SecurityContextBehavior<TRequest, TResponse>(
//    ITenantContext tenantContext,
//    IClientContext clientContext,
//    IApiKeyContext apiKeyContext)
//    : IPipelineBehavior<TRequest, TResponse>
//    where TRequest : notnull
//{
//    public async Task<TResponse> Handle(
//        TRequest request,
//        RequestHandlerDelegate<TResponse> next,
//        CancellationToken cancellationToken)
//    {
//        // Validation stricte des contextes avant toute action
//        // Si X-Tenant-Id ou X-Restaurant-Id est manquant, EnsureResolved()
//        // lèvera une InvalidOperationException capturée par votre Middleware d'Exception.

//        tenantContext.EnsureResolved();
//        clientContext.EnsureResolved();
//        apiKeyContext.EnsureResolved();

//        // Vérification de la validation de la clé API
//        if (!apiKeyContext.IsValidated)
//        {
//            throw new UnauthorizedAccessException("API Key has not been validated.");
//        }

//        return await next(cancellationToken);
//    }
//}
