namespace MicroKit.Result.Samples.Readme;

// Verbatim copy of the README "Core Types" block — keep it identical to README.md.
internal static class CoreTypesSamples
{
    internal static void CoreTypesSample()
    {
Result success = Result.Success();
Result failure = Result.Failure(new MyError());

Result<int> value  = Result.Success(42);
Result<int> failed = Result.Failure<int>(new MyError());

// implicit conversion from T
Result<int> fromValue = 42;
    }

    internal sealed record MyError() : Error(ErrorCode.From("SAMPLE.ERROR"), "Sample error");
}
