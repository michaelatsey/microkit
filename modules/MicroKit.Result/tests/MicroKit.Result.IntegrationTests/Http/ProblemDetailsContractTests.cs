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

    // The core decides which category an ErrorCollection reports without knowing HTTP; this keeps its
    // "server fault" notion in step with the status codes this package maps to 5xx.
    [Theory]
    [MemberData(nameof(Categories))]
    public void ErrorCollectionCategory_ForEveryCategory_PrefersItOverValidationExactlyWhenItMapsTo5xx(ErrorCategory category)
    {
        // Arrange
        var collection = ErrorCollection.From(ValidationError.Required("Name"), new CategorizedTestError(category));

        // Act
        bool reportsTheCategory = collection.Category == category;
        bool mapsTo5xx = ResultProblemDetailsFactory.ToStatusCode(category) >= 500;

        // Assert
        reportsTheCategory.ShouldBe(mapsTo5xx || category == ErrorCategory.Validation);
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
