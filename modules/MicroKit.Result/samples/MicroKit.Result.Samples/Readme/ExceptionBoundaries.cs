using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace MicroKit.Result.Samples.Readme;

// README "Exception Boundaries": its C# blocks are here verbatim, modulo indentation; the rest only makes them build. Keep them identical to README.md.
internal static class ExceptionBoundariesSamples
{
    internal static async Task MapperSample(IUserRepository repository, ILegacyUserClient legacyClient, ILogger logger, Guid id)
    {
        IError ToDatabaseError(Exception ex)
        {
            logger.DatabaseCallFailed(ex);
            return ex is DbException ? new DatabaseError() : new TechnicalError();
        }

        Result<User> result = Result<User>.Try(() => repository.GetUser(id), ToDatabaseError);

        IError ToRemoteCallError(Exception ex)
        {
            logger.RemoteCallFailed(ex);
            return ex switch
            {
                TaskCanceledException { InnerException: TimeoutException } => new RemoteTimeoutError(), // an HttpClient timeout
                HttpRequestException => new RemoteCallError(),
                _ => new TechnicalError(),
            };
        }

        Result<User> loaded = await Result<User>.TryAsync(
            async () => await legacyClient.FetchUserAsync(id), // a third-party API with no CancellationToken
            ToRemoteCallError);
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
            catch (DbException ex) when (!ct.IsCancellationRequested) // the ADO.NET provider's exception; caller cancellation, even one the provider throws as a DbException, and bugs propagate
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

    public sealed record RemoteCallError()
        : Error(ErrorCode.ExternalServiceFailure, "The remote service call failed.")
    {
        public override ErrorCategory Category => ErrorCategory.External;
    }

    public sealed record RemoteTimeoutError()
        : Error(ErrorCode.Timeout, "The remote service did not respond in time.")
    {
        public override ErrorCategory Category => ErrorCategory.Timeout;
    }

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Remote service call failed")]
    public static partial void RemoteCallFailed(this ILogger logger, Exception exception);
}
