namespace MicroKit.Result.Samples.Guide;

// Backs docs/guides/usage.md, "Validate input": its C# blocks are here verbatim, modulo indentation; the rest only
// makes them build. Keep the two identical.
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

    internal static Result<Order> ValidationResultSample(string name, int age, string email)
    {
        var validation = new ValidationResult()
            .AddErrorIf(string.IsNullOrEmpty(name), "Name", "Name is required.")
            .AddErrorIf(age < 0, "Age", "Age must be non-negative.")
            .AddErrorIf(!email.Contains('@'), "Email", "Email is not a valid email address.");

        if (!validation.IsValid)
            return validation.ToResult<Order>(); // a failure holding every validation error

        return CreateOrder(name, age, email);
    }

    internal static Result<Registration> SuccessOrFailureSample(string email, string password)
    {
        return new ValidationResult()
            .AddErrorIf(string.IsNullOrWhiteSpace(email), "Email", "Email is required.")
            .AddErrorIf(password.Length < 8, "Password", "Password must be at least 8 characters.")
            .ToResult(new Registration(email, password)); // the value on success, every error on failure
    }

    internal static Result<EmailAddress> ParseEmail(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ValidationError.Required("Email"); // one validation error converts to a failure
        if (!input.Contains('@'))
            return ValidationError.InvalidEmail();

        return new EmailAddress(input);
    }

    internal sealed record Order;

    internal sealed record EmailAddress(string Value);

    internal sealed record Registration(string Email, string Password);

    private static Result<Order> CreateOrder(string name, int age, string email) => throw new NotSupportedException();
}
