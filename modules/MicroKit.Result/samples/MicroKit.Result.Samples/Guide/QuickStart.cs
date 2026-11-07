using MicroKit.Result;
using MicroKit.Result.AspNetCore;
using Microsoft.AspNetCore.Builder;

namespace MicroKit.Result.Samples.Guide;

// Backs README.md, "Getting started": its C# block is here verbatim, modulo indentation; the rest only makes it
// build. Keep the two identical.
internal static class QuickStartSamples
{
    internal sealed record User(Guid Id, string Name);

    internal sealed class UserDirectory(IReadOnlyDictionary<Guid, User> users)
    {
        private readonly IReadOnlyDictionary<Guid, User> _users = users;

        // A domain error: one type, which says what kind of failure it reports
        public sealed record UserNotFoundError(Guid UserId)
            : Error(ErrorCode.From("USER.NOT_FOUND"), $"User {UserId} not found")
        {
            public override ErrorCategory Category => ErrorCategory.NotFound;
        }

        // A success holds a value; a failure holds an error
        public Result<User> GetUser(Guid id) =>
            _users.TryGetValue(id, out User? user)
                ? Result.Success(user)
                : new UserNotFoundError(id);

        // Compose on the success rail, then consume both rails
        public string Greet(Guid id) =>
            GetUser(id)
                .Map(user => user.Name)
                .Match(
                    onSuccess: name => $"Hello, {name}.",
                    onFailure: error => error.Message);

        // Or answer HTTP: 200 with the user, or problem details with a 404
        public void MapEndpoints(WebApplication app) =>
            app.MapGet("/users/{id}", (Guid id) => GetUser(id).ToHttpResult());
    }
}
