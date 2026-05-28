using System.Linq.Expressions;
using MicroKit.Result;
using ResultStatic = MicroKit.Result.Result;

namespace MicroKit.MediatR;

/// <summary>
/// Mandatory base class for all MicroKit pipeline behaviors. Inherit from this — never implement
/// <c>IPipelineBehavior</c> directly (ADR-002).
/// </summary>
/// <remarks>
/// Provides:
/// <list type="bullet">
/// <item><description><see cref="Order"/> — ties the behavior to the <see cref="PipelineOrder"/> registry.</description></item>
/// <item><description><see cref="IsResultResponse"/> — cached per closed generic; true when <typeparamref name="TResponse"/> is <c>Result&lt;T&gt;</c>.</description></item>
/// <item><description><see cref="CreateFailureOrThrow"/> — returns <c>Result.Failure(error)</c> or throws, depending on <typeparamref name="TResponse"/>.</description></item>
/// </list>
/// </remarks>
/// <typeparam name="TRequest">The request type flowing through the pipeline.</typeparam>
/// <typeparam name="TResponse">The response type produced by the handler.</typeparam>
public abstract class BehaviorBase<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// The pipeline execution order. Use a value from <see cref="PipelineOrder"/>.
    /// Duplicate order values across registered behaviors result in undefined execution order.
    /// </summary>
    public abstract int Order { get; }

    /// <inheritdoc />
    public abstract Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);

    /// <summary>
    /// <see langword="true"/> when <typeparamref name="TResponse"/> is <c>Result&lt;T&gt;</c> for any T.
    /// Computed once per closed generic via static initializer — zero per-request reflection.
    /// </summary>
    protected static readonly bool IsResultResponse = ComputeIsResultResponse();

    private static readonly Func<IError, TResponse>? _createFailure = IsResultResponse ? BuildCreateFailure() : null;

    /// <summary>
    /// Returns a failure <typeparamref name="TResponse"/> when it is <c>Result&lt;T&gt;</c>,
    /// or throws <paramref name="fallbackException"/> when <typeparamref name="TResponse"/> is a direct (non-Result) type.
    /// Call this as the short-circuit return in behaviors that reject requests.
    /// </summary>
    /// <param name="error">The error to wrap in a failure result (used when <typeparamref name="TResponse"/> is <c>Result&lt;T&gt;</c>).</param>
    /// <param name="fallbackException">Exception thrown when <typeparamref name="TResponse"/> is not <c>Result&lt;T&gt;</c>. This method never returns in that case.</param>
    /// <returns>A <typeparamref name="TResponse"/> wrapping <paramref name="error"/> as a failure. Never returns when <typeparamref name="TResponse"/> is a direct type — throws instead.</returns>
    protected static TResponse CreateFailureOrThrow(IError error, Exception fallbackException)
    {
        if (_createFailure is not null)
            return _createFailure(error);

        throw fallbackException;
    }

    private static bool ComputeIsResultResponse()
    {
        var type = typeof(TResponse);
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "ReflectionAnalysis", "IL2060",
        Justification = "ResultStatic.Failure<T> is always present via ProjectReference — type safety is guaranteed by generic type constraints.")]
    private static Func<IError, TResponse> BuildCreateFailure()
    {
        // Called only when IsResultResponse is true — TResponse is guaranteed to be Result<TInner>.
        // Build: (IError error) => ResultStatic.Failure<TInner>(error)
        // where TInner = TResponse's generic argument (e.g. UserDto when TResponse = Result<UserDto>)
        var innerType = typeof(TResponse).GetGenericArguments()[0];
        var failureMethod = typeof(ResultStatic)
            .GetMethods()
            .First(m => m.Name == "Failure" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .MakeGenericMethod(innerType);

        var errorParam = Expression.Parameter(typeof(IError), "error");
        var call = Expression.Call(failureMethod, errorParam);
        var convert = Expression.Convert(call, typeof(TResponse));
        return Expression.Lambda<Func<IError, TResponse>>(convert, errorParam).Compile();
    }
}
