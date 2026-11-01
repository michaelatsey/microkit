using Microsoft.Extensions.Logging;

namespace MicroKit.Result.Samples.Readme;

// README "Exception Boundaries": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class ExceptionBoundariesSamples
{
    internal static async Task MapperSample(IUserRepository repository, ILogger logger, Guid id)
    {
        IError ToDatabaseError(Exception ex)
        {
            logger.DatabaseCallFailed(ex);
            return new DatabaseError();
        }

        Result<User> result = Result<User>.Try(() => repository.GetUser(id), ToDatabaseError);

        Result<User> loaded = await Result<User>.TryAsync(
            async () => await repository.GetUserAsync(id),
            ToDatabaseError);
    }

    internal sealed class UserGateway(IUserRepository repository, ILogger logger)
    {
        public async ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct = default)
        {
            try
            {
                return Result.Success(await repository.GetUserAsync(id, ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.DatabaseCallFailed(ex);
                return Result.Failure<User>(new DatabaseError());
            }
        }
    }

    public sealed record DatabaseError()
        : Error(ErrorCode.From("INFRA.DATABASE.FAILURE"), "A database error occurred.");

    internal sealed record User;

    internal interface IUserRepository
    {
        User GetUser(Guid id);

        Task<User> GetUserAsync(Guid id, CancellationToken ct = default);
    }
}

internal static partial class InfrastructureLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Database call failed")]
    public static partial void DatabaseCallFailed(this ILogger logger, Exception exception);
}
