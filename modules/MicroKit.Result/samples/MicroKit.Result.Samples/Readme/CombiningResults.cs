namespace MicroKit.Result.Samples.Readme;

// Verbatim copy of the README "Combining Results" block — keep it identical to README.md.
internal static class CombiningResultsSamples
{
    internal static void CombiningResultsSample(
        Result validateName, Result validateEmail, Result validateAge,
        Result<User> getUser, Result<Cart> getCart)
    {
// Fail-fast — stops at first failure
Result combined = ResultCombineExtensions.Combine(validateName, validateEmail, validateAge);

// Collect-all — returns ErrorCollection with ALL errors
Result allErrors = ResultCombineExtensions.CombineAll(validateName, validateEmail, validateAge);

// Tuple combination (up to 3)
Result<(User, Cart)> pair = ResultCombineExtensions.Combine(getUser, getCart);
    }

    internal sealed record User;

    internal sealed record Cart;
}
