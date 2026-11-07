using MicroKit.Result;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Result.Samples.Guide;

// Backs docs/guides/usage.md, "Return results over HTTP": its C# blocks are here verbatim, modulo indentation; the
// rest only makes them build. Keep the two identical.
internal static class HttpSamples
{
    internal static void RegistrationSample(WebApplicationBuilder builder)
    {
        // Problem details go through IProblemDetailsService: CustomizeProblemDetails and the traceId apply
        builder.Services.AddProblemDetails();
    }

    internal static void EndpointsSample(WebApplication app)
    {
        // ct is the request's token, cancelled when the client goes away
        app.MapGet("/users/{id}", async (Guid id, IUserService svc, CancellationToken ct) =>
            (await svc.GetUserAsync(id, ct)).ToHttpResult());

        // A command: 204 No Content on success
        app.MapDelete("/users/{id}", async (Guid id, IUserService svc, CancellationToken ct) =>
            (await svc.DeleteUserAsync(id, ct)).ToHttpResult());

        // 201 Created with the location of the new resource; the problem responses declared for OpenAPI
        app.MapPost("/users", async (CreateUser command, IUserService svc, CancellationToken ct) =>
                (await svc.CreateUserAsync(command, ct)).ToCreatedHttpResult(user => $"/users/{user.Id}"))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        // 202 Accepted with the location to poll
        app.MapPost("/exports", async (StartExport command, IExportService exports, CancellationToken ct) =>
                (await exports.StartAsync(command, ct)).ToAcceptedHttpResult(job => $"/exports/{job.Id}"))
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static void FactorySample(IError error)
    {
        // Outside Minimal APIs, for an MVC action or a custom writer
        ProblemDetails problem = ResultProblemDetailsFactory.CreateProblemDetails(error);
        int status = ResultProblemDetailsFactory.ToStatusCode(error.Category);
    }

    internal sealed record User(Guid Id);

    internal sealed record CreateUser(string Email);

    internal sealed record ExportJob(Guid Id);

    internal sealed record StartExport(string Format);

    internal interface IUserService
    {
        ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct = default);

        ValueTask<Result> DeleteUserAsync(Guid id, CancellationToken ct = default);

        ValueTask<Result<User>> CreateUserAsync(CreateUser command, CancellationToken ct = default);
    }

    internal interface IExportService
    {
        ValueTask<Result<ExportJob>> StartAsync(StartExport command, CancellationToken ct = default);
    }
}
