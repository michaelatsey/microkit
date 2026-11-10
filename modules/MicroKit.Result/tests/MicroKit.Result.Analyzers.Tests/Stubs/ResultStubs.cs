namespace MicroKit.Result.Analyzers.Tests.Stubs;

/// <summary>
/// Inline stubs of the MicroKit.Result types the analyzers resolve by metadata name. Prepend to a test source; the
/// appended code uses simple names, already in scope through the file-level usings.
/// </summary>
internal static class ResultStubs
{
    public const string All = """
        using System;
        using System.Threading.Tasks;
        using MicroKit.Result;

        namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }

        namespace MicroKit.Result
        {
            public enum ErrorCategory { NotFound, Validation, Technical, Conflict }

            public interface IError { string Message { get; } ErrorCategory Category { get; } }

            public abstract record Error(string Code, string Message) : IError
            {
                public virtual ErrorCategory Category => ErrorCategory.Technical;
            }

            public readonly struct Result
            {
                public static Result Success() => default;
                public static Result Failure(IError error) => default;
                public static Result<T> Success<T>(T value) => default;
            }

            public readonly struct Result<T> { public T Value => default!; }

            public static class ResultExtensions
            {
                public static Result<T> Tap<T>(this Result<T> result, Action<T> action) => result;
                public static Result<T> TapError<T>(this Result<T> result, Action<IError> action) => result;
                public static Result<T> Finally<T>(this Result<T> result, Action<Result<T>> action) => result;
                public static Result Tap(this Result result, Action action) => result;
                public static Result TapError(this Result result, Action<IError> action) => result;
                public static Result Finally(this Result result, Action<Result> action) => result;
                public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> mapper) => default;
                public static ValueTask<Result<T>> TapAsync<T>(this Result<T> result, Func<T, Task> action) => new(result);
                public static ValueTask<Result<T>> FinallyAsync<T>(this Result<T> result, Func<Result<T>, Task> action) => new(result);
            }
        }

        """;
}
