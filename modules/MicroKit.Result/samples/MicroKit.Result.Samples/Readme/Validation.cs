namespace MicroKit.Result.Samples.Readme;

// Verbatim copies of the README "Validation" blocks — keep them identical to README.md.
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

    internal static void ValidationResultSample(string name, int age)
    {
var validation = new ValidationResult()
    .AddErrorIf(string.IsNullOrEmpty(name), "Name", "Name is required.")
    .AddErrorIf(age < 0, "Age", "Age must be non-negative.")
    .AddError(ValidationError.InvalidEmail());

Result<Order> result = validation.IsValid
    ? CreateOrder(name, age)
    : validation.ToResult<Order>(default!);
    }

    internal sealed record Order;

    private static Result<Order> CreateOrder(string name, int age) => throw new NotSupportedException();
}
