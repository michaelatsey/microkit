using System.Text.Json;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace MicroKit.Result.IntegrationTests.Http;

// #198 review: a result serialized as is names the same errors, with the same message, as the problem details
// ToHttpResult answers; no validation message or propertyName outside a Validation or BusinessRule category.
public sealed class RawEnvelopeFilteringTests
{
    public static TheoryData<string> Cases() => new() { "unauthorized+validation", "technical+notfound+validation", "notfound+validation", "nested-forbidden", "validation+validation", "businessrule+validation" };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Envelope_WhenCollectionIsSerializedAsIs_NamesTheErrorsProblemDetailsName(string name)
    {
        // Arrange
        var collection = Build(name);
        var problem = ResultProblemDetailsFactory.CreateProblemDetails(collection);

        // Act
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Result.Failure(collection)));

        // Assert
        var error = document.RootElement.GetProperty("error");
        var codes = error.GetProperty("errors").EnumerateArray().SelectMany(Codes).ToArray();
        codes.ShouldBe((string[])problem.Extensions["errorCodes"]!);
        error.GetProperty("message").GetString().ShouldBe(problem.Detail);
        var hasPropertyNames = error.GetProperty("errors").EnumerateArray().Any(HasPropertyName);
        hasPropertyNames.ShouldBe(problem is HttpValidationProblemDetails);
    }

    private static IEnumerable<string> Codes(JsonElement error) =>
        error.TryGetProperty("errors", out var inner)
            ? inner.EnumerateArray().SelectMany(Codes)
            : [error.GetProperty("code").GetString()!];

    private static bool HasPropertyName(JsonElement error) =>
        error.TryGetProperty("propertyName", out _)
        || (error.TryGetProperty("errors", out var inner) && inner.EnumerateArray().Any(HasPropertyName));

    private static ErrorCollection Build(string name) => name switch
    {
        "unauthorized+validation" => ErrorCollection.From(Of(ErrorCategory.Unauthorized), ValidationError.Required("Email")),
        "technical+notfound+validation" => ErrorCollection.From(Of(ErrorCategory.Technical), Of(ErrorCategory.NotFound), ValidationError.Required("Email")),
        "notfound+validation" => ErrorCollection.From(ValidationError.Required("Email"), Of(ErrorCategory.NotFound), Of(ErrorCategory.NotFound)),
        "nested-forbidden" => ErrorCollection.From(ErrorCollection.From(Of(ErrorCategory.Forbidden), ValidationError.Required("Email")), Of(ErrorCategory.Forbidden)),
        "validation+validation" => ErrorCollection.From(ValidationError.Required("Email"), ValidationError.InvalidEmail()),
        "businessrule+validation" => ErrorCollection.From(Of(ErrorCategory.BusinessRule), ValidationError.Required("Email")),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static KindError Of(ErrorCategory category) => new(category);

    private sealed record KindError(ErrorCategory Kind) : Error(ErrorCode.From($"TEST.{Kind}".ToUpperInvariant()), $"A {Kind} error.")
    {
        public override ErrorCategory Category => Kind;
    }
}
