using MicroKit.Result;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

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

        // 201 Created with the location of the new resource; problem responses declared for OpenAPI
        app.MapPost("/users", async (CreateUser command, IUserService svc, CancellationToken ct) =>
                (await svc.CreateUserAsync(command, ct)).ToCreatedHttpResult(user => $"/users/{user.Id}"))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }

    internal sealed record User(Guid Id);

    internal sealed record CreateUser(string Email);

    internal interface IUserService
    {
        ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct = default);

        ValueTask<Result> DeleteUserAsync(Guid id, CancellationToken ct = default);

        ValueTask<Result<User>> CreateUserAsync(CreateUser command, CancellationToken ct = default);
    }
}
