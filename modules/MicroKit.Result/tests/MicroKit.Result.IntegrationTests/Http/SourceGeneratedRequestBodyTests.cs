using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using MicroKit.Result.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Result.IntegrationTests.Http;

// #198 review: the usage guide's Native AOT registration reads a Result<OrderDto> request body through its
// source-generated context, not only writes one.
public sealed class SourceGeneratedRequestBodyTests
{
    [Fact]
    public async Task RequestBody_WhenGuideRegistrationIsUsed_IsReadThroughTheSourceGeneratedContext()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(
            app => app.MapPost("/orders", (Result<OrderDto> order) =>
                order.Match(o => $"{o.Id}:{o.Name}", e => e.Code.Value)),
            services => services.ConfigureHttpJsonOptions(GuideRegistration));
        using var body = new StringContent(
            """{"isSuccess":true,"value":{"Id":7,"Name":"Pen"}}""", Encoding.UTF8, "application/json");

        // Act
        using var response = await host.Client.PostAsync(new Uri("/orders", UriKind.Relative), body);
        string text = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        text.ShouldBe("7:Pen");
    }

    // docs/guides/usage.md, "Serialize under Native AOT", Minimal APIs registration
    private static void GuideRegistration(JsonOptions o)
    {
        o.SerializerOptions.TypeInfoResolverChain.Insert(0, RequestBodyJsonContext.Default);
        o.SerializerOptions.Converters.Add(new ResultJsonConverter<OrderDto>());
    }

    internal sealed record OrderDto(int Id, string Name);
}

[JsonSourceGenerationOptions(Converters = [typeof(ResultJsonConverter<SourceGeneratedRequestBodyTests.OrderDto>)])]
[JsonSerializable(typeof(Result<SourceGeneratedRequestBodyTests.OrderDto>))]
[JsonSerializable(typeof(SourceGeneratedRequestBodyTests.OrderDto))]
internal sealed partial class RequestBodyJsonContext : JsonSerializerContext;
