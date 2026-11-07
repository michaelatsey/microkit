namespace MicroKit.Result.Samples.Readme;

// README "Core Types": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class CoreTypesSamples
{
    internal static void CoreTypesSample()
    {
        Result success = Result.Success();
        Result failure = Result.Failure(new MyError());

        Result<int> value = Result.Success(42);
        Result<int> failed = Result.Failure<int>(new MyError());

        // implicit conversion from T
        Result<int> fromValue = 42;

        // implicit conversion from an error: a failure
        Result<int> fromError = new MyError();
        Result commandFailed = new MyError();
    }

    internal sealed record MyError() : Error(ErrorCode.From("SAMPLE.ERROR"), "Sample error")
    {
        public override ErrorCategory Category => ErrorCategory.BusinessRule;
    }
}
