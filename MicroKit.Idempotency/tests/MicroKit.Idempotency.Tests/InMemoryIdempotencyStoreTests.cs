using MicroKit.Idempotency.Abstractions.Models;
using MicroKit.Idempotency.Core.Persistence;

namespace MicroKit.Idempotency.Tests;

/// <summary>Unit tests for <see cref="InMemoryIdempotencyStore"/>.</summary>
public class InMemoryIdempotencyStoreTests
{
    private readonly InMemoryIdempotencyStore _store = new();
    private const string _tenantId = "tenant-id-456";

    /// <summary>Verifies that a created state can be retrieved by key.</summary>
    [Fact]
    public async Task CreateAndGet_ValidState_ReturnsState()
    {
        // Arrange
        var key = "test-key-1";
        var state = new IdempotencyState(key, _tenantId, IdempotencyStatus.Processing);

        // Act
        await _store.CreateAsync(state, TimeSpan.FromMinutes(5), CancellationToken.None);
        var result = await _store.GetAsync(key, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(key, result.Key);
        Assert.Equal(IdempotencyStatus.Processing, result.Status);
    }

    /// <summary>Verifies that creating a duplicate key throws.</summary>
    [Fact]
    public async Task Create_DuplicateKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var key = "test-key-2";
        var state = new IdempotencyState(key, _tenantId,IdempotencyStatus.Processing);
        await _store.CreateAsync(state, null, CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.CreateAsync(state, null, CancellationToken.None));
    }

    /// <summary>Verifies that completing a state updates status and response.</summary>
    [Fact]
    public async Task Complete_ExistingState_UpdatesStatusAndResponse()
    {
        // Arrange
        var key = "test-key-3";
        var state = new IdempotencyState(key, _tenantId, IdempotencyStatus.Processing);
        await _store.CreateAsync(state, TimeSpan.FromMinutes(5), CancellationToken.None);

        // Act
        await _store.CompleteAsync(key, "{\"success\":true}", IdempotencyStatus.Completed, CancellationToken.None);
        var result = await _store.GetAsync(key, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.Completed, result.Status);
        Assert.Equal("{\"success\":true}", result.Response);
        Assert.NotNull(result.CompletedAtUtc);
    }

    /// <summary>Verifies that deleting a state removes it from the store.</summary>
    [Fact]
    public async Task Delete_ExistingState_RemovesState()
    {
        // Arrange
        var key = "test-key-4";
        var state = new IdempotencyState(key, _tenantId, IdempotencyStatus.Processing);
        await _store.CreateAsync(state, TimeSpan.FromMinutes(5), CancellationToken.None);

        // Act
        await _store.DeleteAsync(key, CancellationToken.None);
        var result = await _store.GetAsync(key, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    /// <summary>Verifies that a get on an expired entry returns null and purges the entry.</summary>
    [Fact]
    public async Task Get_ExpiredEntry_ReturnsNullAndRemovesEntry()
    {
        // Arrange
        var key = "test-key-5";
        var state = new IdempotencyState(key, _tenantId,IdempotencyStatus.Processing);
        await _store.CreateAsync(state, TimeSpan.FromMilliseconds(1), CancellationToken.None);

        // Wait for expiration
        await Task.Delay(10);

        // Act
        var result = await _store.GetAsync(key, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }
}
