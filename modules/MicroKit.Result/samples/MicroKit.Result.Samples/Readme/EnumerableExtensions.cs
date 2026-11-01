namespace MicroKit.Result.Samples.Readme;

// README "Enumerable Extensions": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
// The class is generic because the block uses T as an open type parameter.
internal static class EnumerableExtensionsSamples<T>
{
    internal static void EnumerableExtensionsSample(IEnumerable<User> users)
    {
        IEnumerable<Result<T>> results = GetAllResults();

        // Split into successes and failures
        var (successes, failures) = results.Partition();

        // Extract only successes / only failures
        IEnumerable<T> values = results.Successes();
        IEnumerable<IError> errors = results.Failures();

        // Map each element, fail-fast on first error
        Result<IReadOnlyList<Dto>> dtos = users.Traverse(u => MapToDto(u));
    }

    internal sealed record User;

    internal sealed record Dto;

    private static IEnumerable<Result<T>> GetAllResults() => throw new NotSupportedException();

    private static Result<Dto> MapToDto(User user) => throw new NotSupportedException();
}
