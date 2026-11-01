namespace MicroKit.Result.Samples.Readme;

// Verbatim copy of the README "Exception Handling" block — keep it identical to README.md.
internal static class ExceptionHandlingSamples
{
    internal static void ExceptionHandlingSample(Result<int> failedResult)
    {
try
{
    var value = failedResult.Value; // throws ResultException
}
catch (ResultException ex)
{
    // ex.Errors contains the contributing IError list
    var errors = ex.Errors;
}
    }
}
