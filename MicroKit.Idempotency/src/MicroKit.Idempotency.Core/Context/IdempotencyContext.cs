using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Abstractions.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Idempotency.Core.Context;

/// <summary>Async-local idempotency context tracking the active key and its state within the current execution scope.</summary>
public class IdempotencyContext: IIdempotencyContext
{
    private readonly AsyncLocal<ContextHolder> _currentContext = new();
    private readonly ConcurrentDictionary<string, IdempotencyState> _stateCache = new();

    /// <inheritdoc/>
    public string? CurrentKey => _currentContext.Value?.Key;

    /// <inheritdoc/>
    public bool IsIdempotent => !string.IsNullOrWhiteSpace(CurrentKey);

    /// <inheritdoc/>
    public IdempotencyStatus? CurrentStatus =>
        CurrentKey != null && _stateCache.TryGetValue(CurrentKey, out var state)
            ? state.Status
            : null;

    /// <inheritdoc/>
    public IDisposable BeginScope(string key)
    {
        if (_currentContext.Value != null)
        {
            throw new InvalidOperationException("An idempotency scope is already active");
        }

        var contextHolder = new ContextHolder(key);
        _currentContext.Value = contextHolder;

        return new ScopeDisposable(this, key);
    }

    /// <summary>
    /// Updates the state for the current context
    /// </summary>
    public void UpdateState(IdempotencyState state)
    {
        if (CurrentKey != null)
        {
            _stateCache.AddOrUpdate(CurrentKey, state, (_, _) => state);
        }
    }

    /// <summary>
    /// Clears the current context
    /// </summary>
    internal void ClearScope()
    {
        if (_currentContext.Value != null)
        {
            _stateCache.TryRemove(_currentContext.Value.Key, out _);
            _currentContext.Value = null!;
        }
    }

    private class ContextHolder
    {
        public string Key { get; }

        public ContextHolder(string key)
        {
            Key = key;
        }
    }

    private class ScopeDisposable : IDisposable
    {
        private readonly IdempotencyContext _context;
        private readonly string _key;
        private bool _disposed;

        public ScopeDisposable(IdempotencyContext context, string key)
        {
            _context = context;
            _key = key;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_context.CurrentKey == _key)
                {
                    _context.ClearScope();
                }
                _disposed = true;
            }
        }
    }
}
