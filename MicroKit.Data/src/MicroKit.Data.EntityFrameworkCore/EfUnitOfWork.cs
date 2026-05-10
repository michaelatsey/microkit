

using MicroKit.Data.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroKit.Data.EntityFrameworkCore;

public class EfUnitOfWork<TDbCntext> : IUnitOfWork
    where TDbCntext : DbContext
{
    private readonly ILogger<EfUnitOfWork<TDbCntext>> _logger;
    private readonly TDbCntext _context;

    public EfUnitOfWork(TDbCntext context, ILogger<EfUnitOfWork<TDbCntext>> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Sauvegarde des changements dans la base de données");
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la sauvegarde des changements");
            throw;
        }
    }

}
