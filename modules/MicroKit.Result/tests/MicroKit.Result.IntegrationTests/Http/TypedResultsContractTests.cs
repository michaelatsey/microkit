using System.Net;
using System.Text.Json;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Result.IntegrationTests.Http;

// #184 Phase 5: TypedResults, Created and Accepted, the standard problem type, the Timeout status, and a
// non-generic Result returned as is.
public sealed class TypedResultsContractTests
{
    private sealed record Order(int Id);

    private static CategorizedError OrderNotFound() =>
        Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), "The order does not exist.");

    [Fact]
    public async Task ToHttpResult_WhenUsedAsEndpointReturn_DescribesTheSuccessTypeToOpenApi()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/orders/{id}", (int id) => (id == 1 ? Result.Success(new Order(1)) : OrderNotFound()).ToHttpResult()));

        // Act
        var endpoint = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.Single();
        var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();

        // Assert: the success type is described; ProblemHttpResult has no fixed status, so it adds no metadata and
        // an endpoint declares its problem responses with ProducesProblem(status)
        produces.ShouldContain(p => p.StatusCode == StatusCodes.Status200OK && p.Type == typeof(Order));
    }

    [Fact]
    public async Task ToCreatedHttpResult_WhenSuccess_Answers201WithLocationAndValue()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Success(new Order(7)).ToCreatedHttpResult(o => $"/orders/{o.Id}")));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.OriginalString.ShouldBe("/orders/7");
        json.RootElement.GetProperty("id").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task ToCreatedHttpResult_WhenFailure_AnswersProblemDetails()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure<Order>(OrderNotFound()).ToCreatedHttpResult(o => $"/orders/{o.Id}")));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task ToAcceptedHttpResult_WhenSuccess_Answers202()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
        {
            app.MapPost("/jobs", () => Result.Success(new Order(3)).ToAcceptedHttpResult(o => $"/jobs/{o.Id}"));
            app.MapPost("/commands", () => Result.Success().ToAcceptedHttpResult("/commands/status"));
        });

        // Act
        using var job = await host.Client.PostAsync(new Uri("/jobs", UriKind.Relative), null);
        using var command = await host.Client.PostAsync(new Uri("/commands", UriKind.Relative), null);

        // Assert
        job.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        job.Headers.Location!.OriginalString.ShouldBe("/jobs/3");
        command.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        command.Headers.Location!.OriginalString.ShouldBe("/commands/status");
    }

    [Fact]
    public async Task ToHttpResult_WhenFailure_TypeIsTheRfc9110SectionNotAThirdPartySite()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/orders/9", () => Result.Failure<Order>(OrderNotFound()).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/orders/9", UriKind.Relative));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        json.RootElement.GetProperty("type").GetString().ShouldBe("https://tools.ietf.org/html/rfc9110#section-15.5.5");
    }

    [Fact]
    public async Task ToHttpResult_WhenValidationFails_TypeIsTheRfc4918Section()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/users", () => Result.Failure(ValidationError.Required("Email")).ToHttpResult()));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/users", UriKind.Relative), null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        json.RootElement.GetProperty("type").GetString().ShouldBe("https://tools.ietf.org/html/rfc4918#section-11.2");
    }

    [Theory]
    [InlineData(ErrorCategory.TooManyRequests)]
    [InlineData(ErrorCategory.Cancelled)]
    public async Task ToHttpResult_WhenStatusHasNoKnownSection_TypeIsAbsent(ErrorCategory category)
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/x", () => Result.Failure<int>(new CategorizedError(ErrorCode.From("TEST.X"), "Failed.", category)).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/x", UriKind.Relative));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert: RFC 9457 reads an absent type as about:blank
        json.RootElement.TryGetProperty("type", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ToHttpResult_WhenErrorIsTimeout_Answers504()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/quote", () => Result.Failure<int>(Error.Timeout(ErrorCode.Timeout, "The pricing service did not respond in time.")).ToHttpResult()));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/quote", UriKind.Relative));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        ResultProblemDetailsFactory.ToStatusCode(ErrorCategory.Cancelled).ShouldBe(StatusCodes.Status499ClientClosedRequest);
    }

    [Fact]
    public async Task NonGenericResult_WhenReturnedAsIs_IsSerializedWithoutMetadata()
    {
        // Arrange: no converter registered; Result carries its own
        var error = new SecretMetadataError();
        await using var host = await ResultHttpHost.StartAsync(app =>
        {
            app.MapGet("/fail", () => Result.Failure(error));
            app.MapGet("/ok", () => Result.Success());
        });

        // Act
        using var failure = await host.Client.GetAsync(new Uri("/fail", UriKind.Relative));
        string failureBody = await failure.Content.ReadAsStringAsync();
        using var success = await host.Client.GetAsync(new Uri("/ok", UriKind.Relative));
        string successBody = await success.Content.ReadAsStringAsync();

        // Assert
        failureBody.ShouldNotContain("metadata", Case.Insensitive);
        failureBody.ShouldNotContain("internal-host-42");
        using (var json = JsonDocument.Parse(failureBody))
        {
            json.RootElement.GetProperty("isSuccess").GetBoolean().ShouldBeFalse();
            json.RootElement.GetProperty("error").GetProperty("code").GetString().ShouldBe("TEST.SECRET");
        }

        success.StatusCode.ShouldBe(HttpStatusCode.OK);
        successBody.ShouldBe("""{"isSuccess":true}""");
    }

    private sealed record SecretMetadataError() : Error(ErrorCode.From("TEST.SECRET"), "Something failed.")
    {
        public override ErrorCategory Category => ErrorCategory.Technical;

        public override IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Create("host", "internal-host-42");
    }
}
