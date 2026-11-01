using MicroKit.Result;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;

namespace MicroKit.Result.Samples.Readme;

// README "ASP.NET Core": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class AspNetCoreSamples
{
    internal static void AspNetCoreSample(WebApplication app)
    {
        // Minimal API
        app.MapGet("/users/{id}", async (Guid id, IUserService svc) =>
            (await svc.GetUserAsync(id)).ToHttpResult());

        // Result without value (command)
        app.MapDelete("/users/{id}", async (Guid id, IUserService svc) =>
            (await svc.DeleteUserAsync(id)).ToHttpResult());
    }

    internal sealed record User;

    internal interface IUserService
    {
        ValueTask<Result<User>> GetUserAsync(Guid id);

        ValueTask<Result> DeleteUserAsync(Guid id);
    }
}
