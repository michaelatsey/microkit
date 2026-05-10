namespace MicroKit.Data.Abstractions;

public interface ITransactionalContext
{
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);
}
