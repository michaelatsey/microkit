namespace MicroKit.Result.Samples.Readme;

// README "Validation": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class ValidationSamples
{
    internal static void ValidationErrorFactoriesSample()
    {
        ValidationError[] errors =
        [
            ValidationError.Required("Email"),
            ValidationError.MinLength("Password", 8),
            ValidationError.MaxLength("Username", 50),
            ValidationError.StringLength("Bio", 10, 500),
            ValidationError.OutOfRange("Age", min: 0, max: 120),
            ValidationError.InvalidFormat("PostalCode", hint: "5 digits"),
            ValidationError.InvalidEmail(),
            ValidationError.InvalidUrl("WebsiteUrl"),
            ValidationError.Custom("Field", "Custom message"),
        ];
    }

    internal static void ValidationResultSample(string name, int age, string email)
    {
        var validation = new ValidationResult()
            .AddErrorIf(string.IsNullOrEmpty(name), "Name", "Name is required.")
            .AddErrorIf(age < 0, "Age", "Age must be non-negative.")
            .AddErrorIf(!email.Contains('@'), "Email", "Email is not a valid email address.");

        Result<Order> result = validation.IsValid
            ? CreateOrder(name, age, email)
            : Result.Failure<Order>(validation.ToResult().Error);
    }

    internal sealed record Order;

    private static Result<Order> CreateOrder(string name, int age, string email) => throw new NotSupportedException();
}
