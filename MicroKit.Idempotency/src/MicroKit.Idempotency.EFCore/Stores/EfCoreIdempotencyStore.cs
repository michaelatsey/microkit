using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Abstractions.Models;
using MicroKit.Idempotency.EFCore.Configurations;
using MicroKit.Idempotency.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MicroKit.Idempotency.EFCore.Stores;

public class EfCoreIdempotencyStore<TContext> : IIdempotencyStore
    where TContext : DbContext
{
    private readonly TContext _dbContext;
    private readonly EFCoreIdempotencyOptions _storeOptions;
    public EfCoreIdempotencyStore(
        TContext context,
        IOptions<EFCoreIdempotencyOptions> storeOptions)
    {
        _dbContext = context ?? throw new ArgumentNullException(nameof(context));
        _storeOptions = storeOptions.Value;
    }

    public async Task CompleteAsync(string key, string response, IdempotencyStatus status, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.Set<IdempotencyRecord>()
                .FindAsync([key], cancellationToken: cancellationToken);
        if (record != null)
        {
            record.Status = status;
            record.Response = response;
            record.CompletedAtUtc = DateTimeOffset.UtcNow;

            // Renew expiration if configured
            if (_storeOptions is { RenewExpirationOnComplete: true, DefaultTtl: not null })
            {
                record.ExpiresAtUtc = DateTimeOffset.UtcNow.Add(_storeOptions.DefaultTtl.Value);
            }

        }
    }

    public async Task CreateAsync(IdempotencyState state, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        var entry = new IdempotencyRecord
        {
            Key = state.Key,
            TenantId = state.TenantId,
            Status = state.Status,
            Response = state.Response,
            CreatedAtUtc = state.CreatedAtUtc,
            CompletedAtUtc = state.CompletedAtUtc,
            RequestHash = state.RequestHash,
            ExpiresAtUtc = ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : null
        };

        await _dbContext.Set<IdempotencyRecord>().AddAsync(entry, cancellationToken);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.Set<IdempotencyRecord>()
                .FindAsync([key], cancellationToken: cancellationToken);

        if (record is not null)
        {
            _dbContext.Set<IdempotencyRecord>().Remove(record);
        }
    }

    public async Task FailAsync(string key, IdempotencyStatus status, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.Set<IdempotencyRecord>()
            .FindAsync([key], cancellationToken: cancellationToken);
        if (record is not null)
        {
            record.Status = status;
            record.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public async Task<IdempotencyState?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.Set<IdempotencyRecord>()
            .FindAsync([key], cancellationToken: cancellationToken);

        if (entry == null)
        {
            return null;
        }

        // Check if expired
        if (!entry.ExpiresAtUtc.HasValue || entry.ExpiresAtUtc.Value >= DateTimeOffset.UtcNow) return MapToState(entry);
        await DeleteAsync(key, cancellationToken);
        return null;

    }

    public async Task RenewExpirationAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.Set<IdempotencyRecord>()
                .FirstOrDefaultAsync(e => e.Key == key, cancellationToken);

        if (entry is not null)
        { 
            entry.ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ttl);
        }
    }


    private static IdempotencyState MapToState(IdempotencyRecord record)
    {
        return new IdempotencyState(record.Key, record.TenantId, record.Status)
        {
            Response = record.Response,
            CreatedAtUtc = record.CreatedAtUtc,
            CompletedAtUtc = record.CompletedAtUtc,
            RequestHash = record.RequestHash
        };
    }

    // private static bool IsUniqueConstraintViolation (DbUpdateException ex)
    // {
    //     return ex.InnerException?.Message?.Contains("unique constraint") == true ||
    //            ex.InnerException?.Message?.Contains("duplicate key") == true;
    // }
}
