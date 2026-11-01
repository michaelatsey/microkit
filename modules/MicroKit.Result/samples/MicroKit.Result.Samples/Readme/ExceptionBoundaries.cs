namespace MicroKit.Result.Samples.Readme;

// Verbatim copy of the README "Exception Boundaries" block — keep it identical to README.md.
internal static class ExceptionBoundariesSamples
{
    internal static async Task ExceptionBoundariesSample(IUserRepository repository, Guid id)
    {
// Default — wraps Exception in ExceptionError (Technical category)
Result<User> result = Result<User>.Try(() => repository.GetUser(id));

// Custom mapper — convert exception to your domain error
Result<User> loaded = await Result<User>.TryAsync(
    async () => await repository.GetUserAsync(id),
    ex => new DatabaseError(ex));
    }

    internal sealed record User;

    internal sealed record DatabaseError(Exception Exception)
        : Error(ErrorCode.From("INFRA.DATABASE.FAILURE"), "Database call failed");

    internal interface IUserRepository
    {
        User GetUser(Guid id);

        Task<User> GetUserAsync(Guid id);
    }
}
