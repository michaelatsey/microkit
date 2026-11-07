using MicroKit.Result;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;

namespace MicroKit.Result.Samples.Readme;

// README "ASP.NET Core": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class AspNetCoreSamples
{
    internal static void AspNetCoreSample(WebApplication app)
    {
        // Minimal API; ct is the request's token, cancelled when the client goes away
        app.MapGet("/users/{id}", async (Guid id, IUserService svc, CancellationToken ct) =>
            (await svc.GetUserAsync(id, ct)).ToHttpResult());

        // Result without value (command)
        app.MapDelete("/users/{id}", async (Guid id, IUserService svc, CancellationToken ct) =>
            (await svc.DeleteUserAsync(id, ct)).ToHttpResult());
    }

    internal sealed record User;

    internal interface IUserService
    {
        ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct = default);

        ValueTask<Result> DeleteUserAsync(Guid id, CancellationToken ct = default);
    }
}
