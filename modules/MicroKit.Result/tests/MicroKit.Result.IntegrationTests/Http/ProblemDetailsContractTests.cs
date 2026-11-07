using System.Net;
using System.Text.Json;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;

namespace MicroKit.Result.IntegrationTests.Http;

// #184 Phase 2: what a client receives when an endpoint returns a failure through ToHttpResult().
public sealed class ProblemDetailsContractTests
{
    private const string SecretMessage = "Npgsql: password authentication failed for user 'svc' at /srv/app/secrets.json";

    [Fact]
    public async Task ToHttpResult_WhenErrorWrapsAnException_ResponseOmitsTheExceptionMessage()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/boom", () => Result<int>.Try(() => throw new InvalidOperationException(SecretMessage)).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/boom", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldNotContain(SecretMessage);
        body.ShouldNotContain("Npgsql");
        body.ShouldNotContain(nameof(InvalidOperationException));
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("detail").GetString().ShouldBe("An unexpected error occurred.");
        json.RootElement.GetProperty("errorCode").GetString().ShouldBe("EXCEPTION");
    }

    [Fact]
    public async Task ToHttpResult_WhenErrorIsOneValidationError_ResponseGroupsItUnderItsProperty()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => Result.Failure(ValidationError.InvalidEmail()).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var errors = json.RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name).ShouldBe(["Email"]);
        errors.GetProperty("Email").EnumerateArray().Select(m => m.GetString()).ShouldBe(["Email is not a valid email address."]);
    }

    [Fact]
    public async Task ToHttpResult_WhenErrorIsACollection_ResponseGroupsValidationMessagesByProperty()
    {
        // Arrange
        var validation = new ValidationResult()
            .AddError("Email", "Email is required.")
            .AddError("Password", "Password must be at least 8 characters.")
            .AddError("Email", "Email is not a valid email address.");
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => validation.ToResult().ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var errors = json.RootElement.GetProperty("errors");
        errors.GetProperty("Email").EnumerateArray().Select(m => m.GetString())
            .ShouldBe(["Email is required.", "Email is not a valid email address."]);
        errors.GetProperty("Password").EnumerateArray().Select(m => m.GetString())
            .ShouldBe(["Password must be at least 8 characters."]);
    }

    [Fact]
    public async Task ToHttpResult_WhenCollectionMixesValidationAndTechnical_ResponseIs500()
    {
        // Arrange
        var errors = ErrorCollection.From(ValidationError.Required("Name"), new CategorizedTestError(ErrorCategory.Technical));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ToHttpResult_WhenErrorIsNotValidation_ResponseHasNoErrorsMember()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/items/1", () => Result.Failure<int>(new CategorizedTestError(ErrorCategory.NotFound)).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/items/1", UriKind.Relative));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        json.RootElement.TryGetProperty("errors", out _).ShouldBeFalse();
    }

    public static TheoryData<ErrorCategory> Categories()
    {
        var data = new TheoryData<ErrorCategory>();
        foreach (var category in Enum.GetValues<ErrorCategory>())
            data.Add(category);
        data.Add((ErrorCategory)999);
        return data;
    }

    public static TheoryData<ErrorCategory, ErrorCategory> CategoryPairs()
    {
        var categories = Enum.GetValues<ErrorCategory>().Append((ErrorCategory)999).ToArray();
        var data = new TheoryData<ErrorCategory, ErrorCategory>();
        foreach (var first in categories)
            foreach (var second in categories)
                data.Add(first, second);
        return data;
    }

    // The category, hence the status, of an ErrorCollection never depends on the order of its errors.
    [Theory]
    [MemberData(nameof(CategoryPairs))]
    public void ErrorCollectionCategory_ForEveryPairInBothOrders_GivesTheSameStatus(ErrorCategory first, ErrorCategory second)
    {
        // Arrange
        var forward = ErrorCollection.From(new CategorizedTestError(first), new CategorizedTestError(second));
        var backward = ErrorCollection.From(new CategorizedTestError(second), new CategorizedTestError(first));

        // Act
        int forwardStatus = ResultProblemDetailsFactory.ToStatusCode(forward.Category);
        int backwardStatus = ResultProblemDetailsFactory.ToStatusCode(backward.Category);

        // Assert
        forward.Category.ShouldBe(backward.Category);
        forwardStatus.ShouldBe(backwardStatus);
    }

    // A server fault is never hidden behind a client error.
    [Theory]
    [MemberData(nameof(Categories))]
    public void ErrorCollectionCategory_WhenOneErrorMapsTo5xx_Answers5xx(ErrorCategory category)
    {
        // Arrange
        var collection = ErrorCollection.From(ValidationError.Required("Name"), new CategorizedTestError(category), new CategorizedTestError(ErrorCategory.NotFound));

        // Act
        bool mapsTo5xx = ResultProblemDetailsFactory.ToStatusCode(category) >= 500;

        // Assert
        (ResultProblemDetailsFactory.ToStatusCode(collection.Category) >= 500).ShouldBe(mapsTo5xx);
    }

    [Theory]
    [InlineData(ErrorCategory.Unauthorized, HttpStatusCode.Unauthorized)]
    [InlineData(ErrorCategory.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ErrorCategory.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(ErrorCategory.Conflict, HttpStatusCode.Conflict)]
    public async Task ToHttpResult_WhenCollectionMixesValidationWithAClientError_AnswersThatErrorWithoutFieldErrors(
        ErrorCategory category, HttpStatusCode expected)
    {
        // Arrange: the validation error comes first, so order cannot decide the status
        var errors = ErrorCollection.From(ValidationError.Required("Email"), new CategorizedTestError(category));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert: an unauthenticated caller, or a missing resource, never receives field validation detail
        response.StatusCode.ShouldBe(expected);
        json.RootElement.TryGetProperty("errors", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ToHttpResult_WhenCollectionMixesValidationAndBusinessRule_AnswersFieldErrors()
    {
        // Arrange
        var errors = ErrorCollection.From(new CategorizedTestError(ErrorCategory.BusinessRule), ValidationError.Required("Email"));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        json.RootElement.GetProperty("errors").TryGetProperty("Email", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task ToHttpResult_WhenCollectionMixesValidationAndTechnical_ResponseCarriesNoFieldErrors()
    {
        // Arrange
        var errors = ErrorCollection.From(ValidationError.Required("Name"), new CategorizedTestError(ErrorCategory.Technical));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert: a 500 gives the client nothing to correct
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        json.RootElement.TryGetProperty("errors", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ToHttpResult_WhenErrorIsACollection_ResponseListsEveryCodeInOrder()
    {
        // Arrange
        var inner = ErrorCollection.From(new CategorizedTestError(ErrorCategory.Conflict), ValidationError.Required("Name"));
        var errors = ErrorCollection.From(new CategorizedTestError(ErrorCategory.Conflict), inner);
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        json.RootElement.GetProperty("errorCodes").EnumerateArray().Select(c => c.GetString())
            .ShouldBe(["TEST.CATEGORIZED", "TEST.CATEGORIZED", "VALIDATION.NAME"]);
    }

    [Fact]
    public void CreateProblemDetails_WhenValidationErrorHasNullPropertyName_GroupsItUnderEmptyKey()
    {
        // Act
        var problem = ResultProblemDetailsFactory.CreateProblemDetails(new NullPropertyValidationError());

        // Assert
        problem.ShouldBeOfType<Microsoft.AspNetCore.Http.HttpValidationProblemDetails>()
            .Errors[string.Empty].ShouldBe(["Invalid."]);
    }

    private sealed class NullPropertyValidationError : IValidationError
    {
        public string PropertyName => null!;

        public ErrorCode Code => ErrorCode.Validation;

        public string Message => "Invalid.";

        public ErrorCategory Category => ErrorCategory.Validation;

        public ErrorSeverity Severity => ErrorSeverity.Warning;

        public IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Empty;
    }

    private sealed record CategorizedTestError(ErrorCategory Kind)
    : Error(ErrorCode.From("TEST.CATEGORIZED"), "A categorized test error.")
    {
        public override ErrorCategory Category => Kind;
    }
}
