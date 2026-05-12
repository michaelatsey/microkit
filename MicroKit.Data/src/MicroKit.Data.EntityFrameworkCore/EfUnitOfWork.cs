using MicroKit.Data.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroKit.Data.EntityFrameworkCore;

/// <summary>EF Core implementation of <see cref="IUnitOfWork"/>.</summary>
public class EfUnitOfWork<TDbContext> : IUnitOfWork
    where TDbContext : DbContext
{
    private readonly ILogger<EfUnitOfWork<TDbContext>> _logger;
    private readonly TDbContext _context;

    /// <summary>
    /// Initializes a new instance of <see cref="EfUnitOfWork{TDbContext}"/>.
    /// </summary>
    public EfUnitOfWork(TDbContext context, ILogger<EfUnitOfWork<TDbContext>> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Persisting changes to the database");
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while persisting changes");
            throw;
        }
    }
}
