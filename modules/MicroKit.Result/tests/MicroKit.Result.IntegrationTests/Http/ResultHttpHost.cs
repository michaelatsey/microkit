using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Result.IntegrationTests.Http;

// A real ASP.NET Core host on Kestrel, bound to a free loopback port: each test maps the endpoints it
// calls, then sends real HTTP requests. No test-host package is needed (module allowed-package table).
internal sealed class ResultHttpHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ResultHttpHost(WebApplication app, Uri baseAddress)
    {
        _app = app;
        Client = new HttpClient { BaseAddress = baseAddress };
    }

    public HttpClient Client { get; }

    public static async Task<ResultHttpHost> StartAsync(
        Action<IEndpointRouteBuilder> map,
        Action<IServiceCollection>? services = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        services?.Invoke(builder.Services);
        var app = builder.Build();
        map(app);
        await app.StartAsync().ConfigureAwait(false);

        // After StartAsync, Urls holds the address Kestrel bound, port included.
        return new ResultHttpHost(app, new Uri(app.Urls.First()));
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
