//using MediatR;
//using MicroKit.Cqrs.Abstractions.Commands;
//using MicroKit.Cqrs.Abstractions.Dispatchers;
//using MicroKit.Data.Abstractions;
//using MicroKit.Idempotency.Abstractions.Contracts;
//using Microsoft.Extensions.Logging;

//namespace MicroKit.Cqrs.MediatR.Behaviors;

//public class TransactionBehavior<TRequest, TResponse> 
//    : IPipelineBehavior<TRequest, TResponse>
//    where TRequest : ICommand<TResponse>
//{
//    private readonly ITransactionalContext _transactionalContext;

//    private readonly IDomainEventsDispatcher _domainEventsDispatcher;
//    private readonly IIdempotencyAccessor _idempotencyAccessor;
//    private readonly IIdempotencyManager _idempotencyManager;

//    /// <summary>
//    /// The logger
//    /// </summary>
//    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;
//    private readonly IUnitOfWork _unitOfWork;

//    /// <summary>
//    /// Initializes a new instance of the <see cref="TransactionBehavior{TRequest, TResponse}"/> class.
//    /// </summary>
//    /// <param name="unitOfWork">The unit of work.</param>
//    /// <param name="logger">The logger.</param>
//    public TransactionBehavior(
//        ILogger<TransactionBehavior<TRequest, TResponse>> logger,
//        ITransactionalContext transactionalContext, 
//        IUnitOfWork unitOfWork, 
//        IDomainEventsDispatcher domainEventsDispatcher, 
//        IIdempotencyAccessor idempotencyAccessor, 
//        IIdempotencyManager idempotencyManager)
//    {
//        _logger = logger;
//        _transactionalContext = transactionalContext;
//        _unitOfWork = unitOfWork;
//        _domainEventsDispatcher = domainEventsDispatcher;
//        _idempotencyAccessor = idempotencyAccessor;
//        _idempotencyManager = idempotencyManager;
//    }

//    /// <summary>
//    /// Pipeline handler. Perform any additional behavior and await the <paramref name="next" /> delegate as necessary.
//    /// </summary>
//    /// <param name="request">Incoming request.</param>
//    /// <param name="next">Awaitable delegate for the next action in the pipeline. Eventually this delegate represents the handler.</param>
//    /// <param name="cancellationToken">Cancellation token.</param>
//    /// <returns>
//    /// Awaitable task returning the result of the <paramref name="next" />.
//    /// </returns>
//    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
//    {
//        //if (!IsCommand(request))
//        //{
//        //    return await next();
//        //}

//        TResponse response = default!;
//        await _transactionalContext.ExecuteAsync(async ct =>
//        {

//            try
//            {
//                if (_logger.IsEnabled(LogLevel.Debug))
//                {
//                    _logger.LogDebug(
//                    "Transaction started for {Command}",
//                        typeof(TRequest).Name);
//                }

//                // 2. DISPATCH & SAVE (L'étape manquante)
//                var idempotencyKey = string.Empty;
//                if (request is IIdempotentRequest<TResponse> idemRequest && !string.IsNullOrEmpty(idemRequest.IdempotencyKey))
//                {
//                    // Ceci n'est pas un request idempotent, continuer normalement
//                    if (string.IsNullOrEmpty(_idempotencyAccessor.CurrentKey))
//                    {
//                        _idempotencyManager.SetKey(idemRequest.IdempotencyKey);
//                    }
//                }
                
//                response = await next(ct);

                
//                await _domainEventsDispatcher.DispatchEventsAsync(ct);
//                // On appelle le UnitOfWork qui va vider les événements et faire le SaveChanges final
//                // à l'intérieur de la transaction gérée par EfTransactionalContext.
//                await _unitOfWork.SaveChangesAsync(ct);
                
//            }
//            finally
//            {
//                // clear idempotency key
//                _idempotencyManager.Clear();
//                if (_logger.IsEnabled(LogLevel.Debug))
//                {
//                    _logger.LogDebug(
//                        "Transaction finished for {Command}",
//                        typeof(TRequest).Name);
//                }
//            }

            
//        }, cancellationToken);

//        return response;

//    }

//    //private static bool IsCommand(TRequest request)
//    //    => request is ICommand || ImplementsGenericCommand(request);

//    //private static bool ImplementsGenericCommand(object request)
//    //    => request.GetType()
//    //              .GetInterfaces()
//    //              .Any(i =>
//    //                  i.IsGenericType &&
//    //                  i.GetGenericTypeDefinition() == typeof(ICommand<>));
//}

