using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;

namespace MicroKit.Result.Samples.Readme;

// Verbatim copy of the README "ASP.NET Core" C# block — keep it identical to README.md.
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
