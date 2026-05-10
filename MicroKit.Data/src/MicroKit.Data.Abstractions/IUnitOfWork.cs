namespace MicroKit.Data.Abstractions;

/// <summary>
/// Représente
/// </summary>
public interface IUnitOfWork 
{
    /// <summary>
    /// Saves the changes asynchronous.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// 
    /// </returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
