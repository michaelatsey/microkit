using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Result.IntegrationTests.Http;

// #195 item 6: a Result<T> returned as is by an endpoint, with no converter registered (no ConfigureHttpJsonOptions),
// is written as the documented envelope, never by reflection over its properties, and never with metadata.
public sealed class RawResultEndpointTests
{
    private sealed record OrderDto(int Id, string Name);

    [Fact]
    public async Task GenericResult_WhenSuccessIsReturnedAsIs_IsSerializedAsTheEnvelope()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/order", () => Result.Success(new OrderDto(7, "Pen"))));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/order", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(body);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["isSuccess", "value"]);
        json.RootElement.GetProperty("isSuccess").GetBoolean().ShouldBeTrue();
        var value = json.RootElement.GetProperty("value");
        value.GetProperty("id").GetInt32().ShouldBe(7);
        value.GetProperty("name").GetString().ShouldBe("Pen");
    }

    [Fact]
    public async Task GenericResult_WhenFailureIsReturnedAsIs_IsSerializedAsTheEnvelopeWithoutMetadata()
    {
        // Arrange
        var error = new OrderNotFoundWithMetadataError(7);
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/order", () => Result.Failure<OrderDto>(error)));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/order", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        // Assert: a plain value, so 200; ToHttpResult is what maps a failure to problem details
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotContain("metadata", Case.Insensitive);
        body.ShouldNotContain("internal-host-42");
        using var json = JsonDocument.Parse(body);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["isSuccess", "error"]);
        json.RootElement.GetProperty("isSuccess").GetBoolean().ShouldBeFalse();
        var written = json.RootElement.GetProperty("error");
        written.GetProperty("code").GetString().ShouldBe("ORDER.NOT_FOUND");
        written.GetProperty("message").GetString().ShouldBe("Order 7 does not exist.");
        written.GetProperty("category").GetString().ShouldBe("NotFound");
    }

    [Fact]
    public async Task GenericResult_WhenTechnicalFailureIsReturnedAsIs_BodyNeverCarriesItsMessage()
    {
        // Arrange: the envelope written without a registered converter is what a client reads
        const string KnownText = "Npgsql: connection to 10.0.0.12:5432 refused";
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapGet("/order", () => Result.Failure<OrderDto>(Error.Technical(ErrorCode.From("DB.FAIL"), KnownText))));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/order", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        // Assert
        body.ShouldNotContain(KnownText);
        body.ShouldNotContain("Npgsql");
        using var json = JsonDocument.Parse(body);
        var written = json.RootElement.GetProperty("error");
        written.GetProperty("code").GetString().ShouldBe("DB.FAIL");
        written.GetProperty("category").GetString().ShouldBe("Technical");
        written.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NonGenericResult_WhenTechnicalFailureIsReturnedAsIs_BodyNeverCarriesItsMessage()
    {
        // Arrange
        const string KnownText = "Npgsql: connection to 10.0.0.12:5432 refused";
        await using var host = await ResultHttpHost.StartAsync(app =>
            app.MapPost("/orders", () => Result.Failure(Error.Technical(ErrorCode.From("DB.FAIL"), KnownText))));

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), content: null);
        string body = await response.Content.ReadAsStringAsync();

        // Assert
        body.ShouldNotContain(KnownText);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("error").GetProperty("code").GetString().ShouldBe("DB.FAIL");
    }

    [Fact]
    public async Task GenericResult_WhenHostRegistersTheFactoryAndTechnicalFailureIsReturnedAsIs_BodyNeverCarriesItsMessage()
    {
        // Arrange: the registration the usage guide shows for Minimal APIs
        const string KnownText = "Npgsql: connection to 10.0.0.12:5432 refused";
        await using var host = await ResultHttpHost.StartAsync(
            app => app.MapGet("/order", () => Result.Failure<OrderDto>(Error.Technical(ErrorCode.From("DB.FAIL"), KnownText))),
            services => services.ConfigureHttpJsonOptions(o =>
                o.SerializerOptions.Converters.Add(new MicroKit.Result.Serialization.ResultJsonConverterFactory())));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/order", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        // Assert
        body.ShouldNotContain(KnownText);
    }

    private sealed record OrderNotFoundWithMetadataError(int OrderId)
        : Error(ErrorCode.From("ORDER.NOT_FOUND"), $"Order {OrderId} does not exist.")
    {
        public override ErrorCategory Category => ErrorCategory.NotFound;

        public override IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Create("host", "internal-host-42");
    }
}
