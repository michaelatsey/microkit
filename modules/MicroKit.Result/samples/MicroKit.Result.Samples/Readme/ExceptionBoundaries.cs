using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace MicroKit.Result.Samples.Readme;

// README "Exception Boundaries": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class ExceptionBoundariesSamples
{
    internal static async Task MapperSample(IUserRepository repository, ILegacyUserClient legacyClient, ILogger logger, Guid id)
    {
        IError ToInfrastructureError(Exception ex)
        {
            logger.DatabaseCallFailed(ex);
            return ex is DbException ? new DatabaseError() : new TechnicalError();
        }

        Result<User> result = Result<User>.Try(() => repository.GetUser(id), ToInfrastructureError);

        Result<User> loaded = await Result<User>.TryAsync(
            async () => await legacyClient.FetchUserAsync(id), // a third-party API with no CancellationToken
            ToInfrastructureError);
    }

    // ILogger<T>: dependency injection registers the generic logger, not ILogger.
    internal sealed class UserGateway(IUserRepository repository, ILogger<UserGateway> logger)
    {
        public async ValueTask<Result<User>> GetUserAsync(Guid id, CancellationToken ct = default)
        {
            try
            {
                return Result.Success(await repository.GetUserAsync(id, ct).ConfigureAwait(false));
            }
            catch (DbException ex) // the ADO.NET provider's exception; cancellation and bugs propagate
            {
                logger.DatabaseCallFailed(ex);
                return Result.Failure<User>(new DatabaseError());
            }
        }
    }

    public sealed record DatabaseError()
        : Error(ErrorCode.From("INFRA.DATABASE.FAILURE"), "A database error occurred.");

    public sealed record TechnicalError()
        : Error(ErrorCode.Internal, "An internal error occurred.");

    internal sealed record User;

    // Task, not ValueTask: the async delegates of the extensions and of TryAsync take a Task.
    internal interface IUserRepository
    {
        User GetUser(Guid id);

        Task<User> GetUserAsync(Guid id, CancellationToken ct = default);
    }

    internal interface ILegacyUserClient
    {
        Task<User> FetchUserAsync(Guid id);
    }
}

internal static partial class InfrastructureLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Database call failed")]
    public static partial void DatabaseCallFailed(this ILogger logger, Exception exception);
}
