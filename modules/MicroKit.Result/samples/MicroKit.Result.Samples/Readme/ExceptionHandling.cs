namespace MicroKit.Result.Samples.Readme;

// README "Exception Handling": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class ExceptionHandlingSamples
{
    internal static void ExceptionHandlingSample(Result<int> failedResult)
    {
        try
        {
            int value = failedResult.GetValueOrThrow(); // throws ResultException
        }
        catch (ResultException ex)
        {
            // ex.Errors contains the contributing IError list
            var errors = ex.Errors;
        }
    }
}
