using System.Diagnostics.CodeAnalysis;
using MicroKit.Result;

namespace MicroKit.MediatR.Behaviors.Pipeline;

/// <summary>
/// Checks whether a response value is a failed <c>Result&lt;T&gt;</c>.
/// Cached per closed <typeparamref name="TResponse"/> generic — zero per-request reflection.
/// </summary>
internal static class ResultInspector<TResponse>
{
    private static readonly Func<TResponse, bool>? _isFailure = BuildIsFailure();

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="response"/> is a <c>Result&lt;T&gt;</c>
    /// with <c>IsFailure == true</c>. Always <see langword="false"/> when
    /// <typeparamref name="TResponse"/> is not <c>Result&lt;T&gt;</c>.
    /// </summary>
    internal static bool IsFailure(TResponse response) => _isFailure?.Invoke(response) ?? false;

    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2060",
        Justification = "Result<T>.IsFailure is always present via ProjectReference.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Expression.Property targets Result<T>.IsFailure which is preserved via ProjectReference.")]
    private static Func<TResponse, bool>? BuildIsFailure()
    {
        var type = typeof(TResponse);
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Result<>))
            return null;

        var param = Expression.Parameter(type, "r");
        // Result<object> resolves to MicroKit.Result.Result<object> (the generic struct) — not the static factory.
        var prop = Expression.Property(param, nameof(Result<object>.IsFailure));
        return Expression.Lambda<Func<TResponse, bool>>(prop, param).Compile();
    }
}
