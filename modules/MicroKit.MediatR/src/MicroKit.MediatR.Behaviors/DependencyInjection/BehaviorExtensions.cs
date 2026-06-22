using System.Diagnostics.CodeAnalysis;
using MicroKit.MediatR.Behaviors.Idempotency;
using MicroKit.MediatR.Behaviors.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroKit.MediatR.Behaviors.DependencyInjection;

/// <summary>
/// Extension methods for activating MicroKit.MediatR built-in behaviors via <see cref="MediatRBuilder"/>.
/// Call these inside the <c>AddMicroKitMediatR</c> callback in <see cref="PipelineOrder"/> sequence
/// so behavior intent is visible at a glance:
/// <see cref="AddLoggingBehavior"/> first, then authorization, validation, idempotency, caching,
/// and <see cref="AddRetryBehavior"/> last.
/// </summary>
/// <example>
/// <code>
/// services.AddMicroKitMediatR(builder => builder
///     .FromAssemblyContaining&lt;MyHandler&gt;()
///     .AddLoggingBehavior()
///     .AddAuthorizationBehavior()
///     .AddValidationBehavior()
///     .AddIdempotencyBehavior()
///     .AddCachingBehavior()
///     .AddRetryBehavior());
/// </code>
/// </example>
public static class BehaviorExtensions
{
    /// <summary>
    /// Activates <see cref="LoggingBehavior{TRequest,TResponse}"/> (pipeline order
    /// <see cref="PipelineOrder.Logging"/> = 100).
    /// Every request is logged with its name, duration, and outcome. This is the only behavior
    /// that applies unconditionally — no opt-in marker is required. Register it first so that
    /// authorization and validation failures are also recorded.
    /// </summary>
    /// <param name="builder">The <see cref="MediatRBuilder"/> to configure.</param>
    /// <returns>The builder for chaining.</returns>
    [RequiresUnreferencedCode("Registering open-generic behaviors uses reflection and is not trim-compatible.")]
    [RequiresDynamicCode("Registering open-generic behaviors calls MakeGenericType at dispatch time and is not NativeAOT-compatible.")]
    public static MediatRBuilder AddLoggingBehavior(this MediatRBuilder builder)
        => builder.AddOpenBehavior(typeof(LoggingBehavior<,>));

    /// <summary>
    /// Activates <see cref="AuthorizationBehavior{TRequest,TResponse}"/> (pipeline order
    /// <see cref="PipelineOrder.Authorization"/> = 200). Opt-in via <see cref="IAuthorizedRequest"/>.
    /// </summary>
    /// <remarks>
    /// Requires two additional DI registrations the caller must supply:
    /// <list type="bullet">
    /// <item><description><c>IAuthorizationService</c> — register via <c>services.AddAuthorization()</c>.</description></item>
    /// <item><description><see cref="ICurrentUserAccessor"/> — provide an implementation appropriate for
    /// the host environment. ASP.NET Core apps can use <c>HttpContextCurrentUserAccessor</c>;
    /// worker services and message consumers must implement their own.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="builder">The <see cref="MediatRBuilder"/> to configure.</param>
    /// <returns>The builder for chaining.</returns>
    [RequiresUnreferencedCode("Registering open-generic behaviors uses reflection and is not trim-compatible.")]
    [RequiresDynamicCode("Registering open-generic behaviors calls MakeGenericType at dispatch time and is not NativeAOT-compatible.")]
    public static MediatRBuilder AddAuthorizationBehavior(this MediatRBuilder builder)
        => builder.AddOpenBehavior(typeof(AuthorizationBehavior<,>));

    /// <summary>
    /// Activates <see cref="ValidationBehavior{TRequest,TResponse}"/> (pipeline order
    /// <see cref="PipelineOrder.Validation"/> = 300).
    /// The behavior is a no-op for request types that have no registered <c>IValidator&lt;TRequest&gt;</c>.
    /// Register FluentValidation validators beforehand via <c>services.AddValidatorsFromAssembly(...)</c>.
    /// </summary>
    /// <param name="builder">The <see cref="MediatRBuilder"/> to configure.</param>
    /// <returns>The builder for chaining.</returns>
    [RequiresUnreferencedCode("Registering open-generic behaviors uses reflection and is not trim-compatible.")]
    [RequiresDynamicCode("Registering open-generic behaviors calls MakeGenericType at dispatch time and is not NativeAOT-compatible.")]
    public static MediatRBuilder AddValidationBehavior(this MediatRBuilder builder)
        => builder.AddOpenBehavior(typeof(ValidationBehavior<,>));

    /// <summary>
    /// Activates <see cref="IdempotencyBehavior{TRequest,TResponse}"/> (pipeline order
    /// <see cref="PipelineOrder.Idempotency"/> = 400) and registers
    /// <see cref="DistributedCacheIdempotencyStore"/> as the default <see cref="IIdempotencyStore"/>
    /// when no other implementation is already registered. Opt-in via <see cref="IIdempotentCommand"/>;
    /// queries and stream queries pass through.
    /// </summary>
    /// <remarks>
    /// Requires <c>IDistributedCache</c> in DI (e.g. <c>services.AddStackExchangeRedisCache(...)</c>
    /// or <c>services.AddDistributedMemoryCache()</c> for local development).
    /// When <c>TResponse</c> is <c>Result&lt;T&gt;</c>, register <c>ResultJsonConverterFactory</c> in
    /// <c>IOptions&lt;JsonSerializerOptions&gt;</c> (see ADR-007).
    /// </remarks>
    /// <param name="builder">The <see cref="MediatRBuilder"/> to configure.</param>
    /// <returns>The builder for chaining.</returns>
    [RequiresUnreferencedCode("Registering open-generic behaviors uses reflection and is not trim-compatible.")]
    [RequiresDynamicCode("Registering open-generic behaviors calls MakeGenericType at dispatch time and is not NativeAOT-compatible.")]
    public static MediatRBuilder AddIdempotencyBehavior(this MediatRBuilder builder)
    {
        builder.Services.TryAddScoped<IIdempotencyStore, DistributedCacheIdempotencyStore>();
        return builder.AddOpenBehavior(typeof(IdempotencyBehavior<,>));
    }

    /// <summary>
    /// Activates <see cref="CachingBehavior{TRequest,TResponse}"/> (pipeline order
    /// <see cref="PipelineOrder.Caching"/> = 500). Opt-in via <see cref="ICacheableQuery"/>;
    /// commands and stream queries pass through.
    /// </summary>
    /// <remarks>
    /// Requires <c>IDistributedCache</c> in DI.
    /// When <c>TResponse</c> is <c>Result&lt;T&gt;</c>, register <c>ResultJsonConverterFactory</c> in
    /// <c>IOptions&lt;JsonSerializerOptions&gt;</c> (see ADR-007).
    /// </remarks>
    /// <param name="builder">The <see cref="MediatRBuilder"/> to configure.</param>
    /// <returns>The builder for chaining.</returns>
    [RequiresUnreferencedCode("Registering open-generic behaviors uses reflection and is not trim-compatible.")]
    [RequiresDynamicCode("Registering open-generic behaviors calls MakeGenericType at dispatch time and is not NativeAOT-compatible.")]
    public static MediatRBuilder AddCachingBehavior(this MediatRBuilder builder)
        => builder.AddOpenBehavior(typeof(CachingBehavior<,>));

    /// <summary>
    /// Activates <see cref="RetryBehavior{TRequest,TResponse}"/> (pipeline order
    /// <see cref="PipelineOrder.Retry"/> = 600). Opt-in via <see cref="IRetryableRequest"/>.
    /// Uses a Polly <see cref="ResiliencePipeline"/> with exponential back-off and jitter.
    /// </summary>
    /// <remarks>
    /// The Polly pipeline is cached per request type — <see cref="IRetryableRequest.MaxRetries"/>
    /// and <see cref="IRetryableRequest.Delay"/> are read from the first dispatched instance of
    /// each request type and treated as type-level constants.
    /// </remarks>
    /// <param name="builder">The <see cref="MediatRBuilder"/> to configure.</param>
    /// <returns>The builder for chaining.</returns>
    [RequiresUnreferencedCode("Registering open-generic behaviors uses reflection and is not trim-compatible.")]
    [RequiresDynamicCode("Registering open-generic behaviors calls MakeGenericType at dispatch time and is not NativeAOT-compatible.")]
    public static MediatRBuilder AddRetryBehavior(this MediatRBuilder builder)
        => builder.AddOpenBehavior(typeof(RetryBehavior<,>));

    /// <summary>
    /// Activates <see cref="TransactionBehavior{TRequest,TResponse}"/> (pipeline order
    /// <see cref="PipelineOrder.Transaction"/> = 700). Commands only — queries and events pass through.
    /// Wraps the command handler in a database transaction: opens before the handler, dispatches
    /// domain events after the handler, commits on success, rolls back on any exception.
    /// </summary>
    /// <remarks>
    /// Requires <see cref="MicroKit.Persistence.Abstractions.ITransactionalContext"/> in DI — provided by
    /// <c>MicroKit.Persistence.EntityFrameworkCore</c> via <c>AddEntityFrameworkCore()</c>.
    /// Register <c>AddTransactionBehavior()</c> last (after <see cref="AddRetryBehavior"/>) so
    /// that Retry (order 600) wraps the entire transactional unit, allowing retry of transient
    /// DB failures without leaving a partial transaction open.
    /// </remarks>
    /// <param name="builder">The <see cref="MediatRBuilder"/> to configure.</param>
    /// <returns>The builder for chaining.</returns>
    [RequiresUnreferencedCode("Registering open-generic behaviors uses reflection and is not trim-compatible.")]
    [RequiresDynamicCode("Registering open-generic behaviors calls MakeGenericType at dispatch time and is not NativeAOT-compatible.")]
    public static MediatRBuilder AddTransactionBehavior(this MediatRBuilder builder)
        => builder.AddOpenBehavior(typeof(TransactionBehavior<,>));
}
