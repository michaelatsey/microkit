namespace MicroKit.Result.Samples.Guide;

// Backs docs/guides/usage.md, "Combine results": its C# blocks are here verbatim, modulo indentation; the rest only
// makes them build. Keep the two identical.
internal static class CombiningSamples
{
    internal static void CombineSample(
        Result validateName, Result validateEmail, Result validateAge,
        Result<User> getUser, Result<Cart> getCart)
    {
        // Fail fast: the first failure
        Result combined = Result.Combine(validateName, validateEmail, validateAge);

        // Collect all: one ErrorCollection holding every error
        Result allErrors = Result.CombineAll(validateName, validateEmail, validateAge);

        // Values into a tuple, for two or three results
        Result<(User, Cart)> pair = Result.Combine(getUser, getCart);
    }

    internal static void ErrorCollectionSample(IError error1, IError error2, IError error3)
    {
        var collection = ErrorCollection.From(error1, error2, error3);

        // A filter may match nothing, so it returns a list, never an empty collection
        IReadOnlyList<IError> validation = collection.WithCategory(ErrorCategory.Validation);
        IReadOnlyList<IError> critical = collection.WithSeverity(ErrorSeverity.Critical);
        IReadOnlyList<IError> notFound = collection.WithCode(ErrorCode.NotFound);
        IReadOnlyList<ValidationError> typed = collection.OfType<ValidationError>();
        bool hasValidation = collection.HasCategory(ErrorCategory.Validation);

        // Group
        IReadOnlyDictionary<ErrorCategory, IReadOnlyList<IError>> byCategory = collection.GroupByCategory();
        IReadOnlyDictionary<ErrorCode, IReadOnlyList<IError>> byCode = collection.GroupByCode();

        // Flatten nested collections
        ErrorCollection flat = collection.Flatten();
    }

    internal static void SequenceSample(IReadOnlyList<Guid> orderIds)
    {
        List<Result<Order>> results = orderIds.Select(FindOrder).ToList();

        // Split into values and errors
        var (orders, errors) = results.Partition();

        // Only the values, or only the errors
        IEnumerable<Order> found = results.Successes();
        IEnumerable<IError> failures = results.Failures();

        // Map each element; fail fast on the first error
        Result<IReadOnlyList<OrderDto>> dtos = orderIds.Traverse(id => FindOrder(id).Map(o => o.ToDto()));
    }

    internal sealed record User;

    internal sealed record Cart;

    internal sealed record Order(Guid Id)
    {
        public OrderDto ToDto() => new(Id);
    }

    internal sealed record OrderDto(Guid Id);

    private static Result<Order> FindOrder(Guid id) => throw new NotSupportedException();
}
