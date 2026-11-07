using System.Net;
using System.Text.Json;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;

namespace MicroKit.Result.IntegrationTests.Http;

// #195 item 1: a 5xx response never carries the error's message. Its detail is a generic, client-safe text, the
// same for every server category; errorCode (and errorCodes for a collection) is kept. A 4xx response keeps the
// error's message as its detail.
public sealed class ServerErrorDetailTests
{
    private const string KnownText = "Npgsql: connection to 10.0.0.12:5432 refused";

    private const string OtherKnownText = "Redis: READONLY You can't write against a read only replica at 10.0.0.31:6379";

    [Fact]
    public async Task ToHttpResult_WhenTechnicalErrorCarriesAnInternalText_ResponseOmitsItAndKeepsTheCode()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/orders", () => Result.Failure<int>(Error.Technical(ErrorCode.From("DB.FAIL"), KnownText)).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/orders", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldNotContain(KnownText);
        body.ShouldNotContain("Npgsql");
        body.ShouldNotContain("10.0.0.12");
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("errorCode").GetString().ShouldBe("DB.FAIL");
    }

    [Theory]
    [InlineData(ErrorCategory.Technical, HttpStatusCode.InternalServerError)]
    [InlineData(ErrorCategory.NotSupported, HttpStatusCode.NotImplemented)]
    [InlineData(ErrorCategory.External, HttpStatusCode.BadGateway)]
    [InlineData(ErrorCategory.Unavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ErrorCategory.Timeout, HttpStatusCode.GatewayTimeout)]
    [InlineData((ErrorCategory)999, HttpStatusCode.InternalServerError)]
    public async Task ToHttpResult_WhenCategoryMapsTo5xx_ResponseOmitsTheMessage(ErrorCategory category, HttpStatusCode expected)
    {
        // Arrange
        var error = new ServerTestError(category, KnownText);
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/quote", () => Result.Failure<int>(error).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/quote", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.ShouldBe(expected);
        body.ShouldNotContain(KnownText);
        using var json = JsonDocument.Parse(body);
        (json.RootElement.GetProperty("detail").GetString() ?? string.Empty).ShouldNotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("errorCode").GetString().ShouldBe("TEST.SERVER");
    }

    [Theory]
    [InlineData(ErrorCategory.Technical)]
    [InlineData(ErrorCategory.NotSupported)]
    [InlineData(ErrorCategory.External)]
    [InlineData(ErrorCategory.Unavailable)]
    [InlineData(ErrorCategory.Timeout)]
    [InlineData((ErrorCategory)999)]
    public void CreateProblemDetails_WhenCategoryMapsTo5xx_DetailIsGenericAndCodeIsKept(ErrorCategory category)
    {
        // Arrange
        var error = new ServerTestError(category, KnownText);

        // Act
        var problem = ResultProblemDetailsFactory.CreateProblemDetails(error);

        // Assert
        string detail = problem.Detail ?? string.Empty;
        (problem.Status >= 500).ShouldBeTrue();
        detail.ShouldNotBeNullOrWhiteSpace();
        detail.ShouldNotContain(KnownText);
        detail.ShouldNotBe(error.Message);
        problem.Extensions["errorCode"].ShouldBe("TEST.SERVER");
    }

    [Fact]
    public void CreateProblemDetails_ForEvery5xxCategory_DetailIsTheSameText()
    {
        // Arrange: every category the factory maps to 5xx, an undefined value included
        var serverCategories = Enum.GetValues<ErrorCategory>()
            .Append((ErrorCategory)999)
            .Where(c => ResultProblemDetailsFactory.ToStatusCode(c) >= 500)
            .ToArray();

        // Act
        var details = serverCategories
            .Select(c => ResultProblemDetailsFactory.CreateProblemDetails(new ServerTestError(c, $"{KnownText} ({c})")).Detail)
            .ToArray();

        // Assert
        serverCategories.ShouldBe(
            [ErrorCategory.Technical, ErrorCategory.Unavailable, ErrorCategory.External, ErrorCategory.NotSupported, ErrorCategory.Timeout, (ErrorCategory)999],
            ignoreOrder: true);
        details.ShouldAllBe(d => !string.IsNullOrWhiteSpace(d));
        details.ShouldAllBe(d => !d!.Contains(KnownText, StringComparison.Ordinal)); // Safe: every detail checked non-null on the line above
        details.Distinct(StringComparer.Ordinal).Count().ShouldBe(1);
    }

    [Theory]
    [InlineData(ErrorCategory.NotFound)]
    [InlineData(ErrorCategory.Validation)]
    [InlineData(ErrorCategory.Unauthorized)]
    [InlineData(ErrorCategory.Forbidden)]
    [InlineData(ErrorCategory.Conflict)]
    [InlineData(ErrorCategory.TooManyRequests)]
    [InlineData(ErrorCategory.BusinessRule)]
    [InlineData(ErrorCategory.Cancelled)]
    [InlineData(ErrorCategory.PreconditionFailed)]
    public void CreateProblemDetails_WhenCategoryMapsTo4xx_DetailIsTheErrorMessage(ErrorCategory category)
    {
        // Arrange
        var error = new ServerTestError(category, "The order 7 cannot be shipped yet.");

        // Act
        var problem = ResultProblemDetailsFactory.CreateProblemDetails(error);

        // Assert
        (problem.Status < 500).ShouldBeTrue();
        problem.Detail.ShouldBe(error.Message);
        problem.Extensions["errorCode"].ShouldBe("TEST.SERVER");
    }

    [Fact]
    public async Task ToHttpResult_WhenCollectionIs5xx_ResponseOmitsEveryMessageAndKeepsTheCodes()
    {
        // Arrange
        var errors = ErrorCollection.From(
            Error.Technical(ErrorCode.From("DB.FAIL"), KnownText),
            Error.Technical(ErrorCode.From("CACHE.FAIL"), OtherKnownText));
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure(errors).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null);
        string body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldNotContain(KnownText);
        body.ShouldNotContain(OtherKnownText);
        body.ShouldNotContain("Npgsql");
        body.ShouldNotContain("Redis");
        using var json = JsonDocument.Parse(body);
        (json.RootElement.GetProperty("detail").GetString() ?? string.Empty).ShouldNotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("errorCodes").EnumerateArray().Select(c => c.GetString())
            .ShouldBe(["DB.FAIL", "CACHE.FAIL"]);
    }

    [Fact]
    public void CreateProblemDetails_WhenCollectionIs5xx_DetailIsTheGenericServerText()
    {
        // Arrange
        var errors = ErrorCollection.From(
            Error.Technical(ErrorCode.From("DB.FAIL"), KnownText),
            ValidationError.Required("Email"),
            Error.Technical(ErrorCode.From("CACHE.FAIL"), OtherKnownText));
        string? generic = ResultProblemDetailsFactory.CreateProblemDetails(new ServerTestError(ErrorCategory.Technical, "Single.")).Detail;

        // Act
        var problem = ResultProblemDetailsFactory.CreateProblemDetails(errors);

        // Assert
        string detail = problem.Detail ?? string.Empty;
        problem.Status.ShouldBe(500);
        detail.ShouldNotBeNullOrWhiteSpace();
        detail.ShouldBe(generic);
        detail.ShouldNotContain(KnownText);
        detail.ShouldNotContain(OtherKnownText);
        problem.Extensions["errorCodes"].ShouldBeOfType<string[]>().ShouldBe(["DB.FAIL", "CACHE.FAIL"]);
    }

    // Its category is a constructor parameter, an undefined value included (CategorizedError rejects one).
    private sealed record ServerTestError(ErrorCategory Kind, string Text)
        : Error(ErrorCode.From("TEST.SERVER"), Text)
    {
        public override ErrorCategory Category => Kind;
    }
}
