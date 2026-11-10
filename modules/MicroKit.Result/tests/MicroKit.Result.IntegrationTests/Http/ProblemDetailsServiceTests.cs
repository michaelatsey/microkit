using System.Net;
using System.Text.Json;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Result.IntegrationTests.Http;

// #184 Phase 5: problem details go through the host's IProblemDetailsService, so CustomizeProblemDetails and
// the traceId it adds apply to a failure returned by ToHttpResult().
public sealed class ProblemDetailsServiceTests
{
    [Fact]
    public async Task ToHttpResult_WhenHostRegistersProblemDetails_ResponseCarriesItsCustomization()
    {
        // Arrange
        await using var host = await ResultHttpHost.StartAsync(
            app => app.MapGet("/items/1", () => Result.Failure<int>(Error.NotFound(ErrorCode.From("ITEM.NOT_FOUND"), "Item 1 does not exist.")).ToHttpResult()),
            services => services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx => ctx.ProblemDetails.Extensions["nodeId"] = "node-7"));

        // Act
        using var response = await host.Client.GetAsync(new Uri("/items/1", UriKind.Relative));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        json.RootElement.GetProperty("nodeId").GetString().ShouldBe("node-7");
        json.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
        json.RootElement.GetProperty("errorCode").GetString().ShouldBe("ITEM.NOT_FOUND");
    }
}
