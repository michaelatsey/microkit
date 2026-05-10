using System.Text.Json;
using MicroKit.Abstractions.Serialization;
using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Abstractions.Models;
using MicroKit.Idempotency.Core.Configuration;
using MicroKit.Idempotency.Core.Exceptions;
using MicroKit.Idempotency.Core.Hashing;
using MicroKit.Idempotency.MediatR.Behaviors;
using MicroKit.Idempotency.Tests.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace MicroKit.Idempotency.Tests;

public class IdempotencyBehaviorTests
{
    private readonly Mock<IIdempotencyStore> _storeMock;
    private readonly Mock<IMicroKitSerializer> _serializerMock;
    private readonly RequestHasher _hasher;
    private readonly IdempotencyOptions _options;
    private readonly IdempotencyBehavior<TestCommand, TestResponse> _behavior;
    private const string TenantId = "tenant-id-456";
    public IdempotencyBehaviorTests()
    {
        _storeMock = new Mock<IIdempotencyStore>();
        _serializerMock = new Mock<IMicroKitSerializer>();
        var contextMock = new Mock<IIdempotencyContext>();
        var idempotencyManager = new Mock<IIdempotencyManager>();
        _serializerMock.Setup(x => x.Serialize(It.IsAny<TestCommand>()))
            .Returns((TestCommand cmd) => JsonSerializer.Serialize(cmd));

        _hasher = new RequestHasher(_serializerMock.Object);
        _options = new IdempotencyOptions();

        _behavior = new IdempotencyBehavior<TestCommand, TestResponse>(
            _storeMock.Object,
            _serializerMock.Object,
            contextMock.Object,
            _hasher,
            Options.Create(_options),
            NullLogger<IdempotencyBehavior<TestCommand, TestResponse>>.Instance,
            idempotencyManager.Object);

        // Setup context mock
        contextMock.Setup(x => x.BeginScope(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>());
    }

    [Fact]
    public async Task Handle_NewRequest_CreatesStoreEntryAndReturnsResponse()
    {
        // Arrange
        const string key = "test-id-123";
        var command = new TestCommand { IdempotencyKey = key, Data = "test" };
        var expectedResponse = new TestResponse { Success = true, Message = "Completed" };

        _storeMock.Setup(x => x.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyState?)null);

        _storeMock.Setup(x => x.CreateAsync(
                It.Is<IdempotencyState>(s => s.Key == key && s.Status == IdempotencyStatus.Processing),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _storeMock.Setup(x => x.CompleteAsync(
                key,
                It.IsAny<string>(),
                IdempotencyStatus.Completed,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _behavior.Handle(
            command,
            _ => Task.FromResult(expectedResponse),
            CancellationToken.None);

        // Assert
        Assert.Equal(expectedResponse, result);
        _storeMock.Verify(x => x.CreateAsync(
            It.Is<IdempotencyState>(s => s.Key == key && s.Status == IdempotencyStatus.Processing),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
        _storeMock.Verify(x => x.CompleteAsync(
            key,
            It.IsAny<string>(),
            IdempotencyStatus.Completed,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingCompletedRequest_ReturnsCachedResponse()
    {
        // Arrange
        const string key = "test-id-456";
        var command = new TestCommand { IdempotencyKey = key, Data = "test" };
        var cachedResponse = new TestResponse { Success = true, Message = "Cached" };
        const string serializedResponse = "{\"success\":true,\"message\":\"Cached\"}";

        var existingState = new IdempotencyState(key, TenantId, IdempotencyStatus.Completed)
        {
            Response = serializedResponse,
            RequestHash = _hasher.ComputeHash(command)
        };

        _storeMock.Setup(x => x.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);

        _serializerMock.Setup(x => x.Deserialize<TestResponse>(serializedResponse))
            .Returns(cachedResponse);

        // Act
        var result = await _behavior.Handle(
            command,
            _ => throw new InvalidOperationException("Should not execute"),
            CancellationToken.None);

        // Assert
        Assert.Equal(cachedResponse, result);
        _storeMock.Verify(x => x.GetAsync(key, It.IsAny<CancellationToken>()), Times.Once);
        _serializerMock.Verify(x => x.Deserialize<TestResponse>(serializedResponse), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingInProgressRequest_ThrowsIdempotencyInProgressException()
    {
        // Arrange
        const string key = "test-id-789";
        var command = new TestCommand { IdempotencyKey = key };

        var existingState = new IdempotencyState(key,TenantId, IdempotencyStatus.Processing);

        _storeMock.Setup(x => x.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);

        // Act & Assert
        await Assert.ThrowsAsync<IdempotencyProgressingException>(() =>
            _behavior.Handle(
                command,
                _ => Task.FromResult(new TestResponse()),
                CancellationToken.None));
    }

    [Fact]
    public async Task Handle_RequestHashMismatch_ThrowsIdempotencyConflictException()
    {
        // Arrange
        _options.VerifyRequestHashes = true;

        const string key = "test-id-conflict";
        var command = new TestCommand { IdempotencyKey = key, Data = "different" };

        var existingState = new IdempotencyState(key, TenantId, IdempotencyStatus.Completed)
        {
            RequestHash = "original-hash"
        };

        _storeMock.Setup(x => x.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);

        // Act & Assert
        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            _behavior.Handle(
                command,
                _ => Task.FromResult(new TestResponse()),
                CancellationToken.None));
    }

    [Fact]
    public async Task Handle_EmptyIdempotencyKey_SkipsProcessing()
    {
        // Arrange
        var command = new TestCommand { IdempotencyKey = "", Data = "test" };
        var expectedResponse = new TestResponse { Success = true };

        // Act
        var result = await _behavior.Handle(
            command,
            _ => Task.FromResult(expectedResponse),
            CancellationToken.None);

        // Assert
        Assert.Equal(expectedResponse, result);
        _storeMock.Verify(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _storeMock.Verify(x => x.CreateAsync(It.IsAny<IdempotencyState>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NullIdempotencyKey_SkipsProcessing()
    {
        // Arrange
        var command = new TestCommand { IdempotencyKey = null!, Data = "test" };
        var expectedResponse = new TestResponse { Success = true };

        // Act
        var result = await _behavior.Handle(
            command,
            _ => Task.FromResult(expectedResponse),
            CancellationToken.None);

        // Assert
        Assert.Equal(expectedResponse, result);
        _storeMock.Verify(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_FailedOperation_MarksAsFailed()
    {
        // Arrange
        var key = "test-id-failed";
        var command = new TestCommand { IdempotencyKey = key, Data = "test" };
        var expectedException = new InvalidOperationException("Operation failed");

        _storeMock.Setup(x => x.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyState?)null);

        _storeMock.Setup(x => x.CreateAsync(
                It.Is<IdempotencyState>(s => s.Key == key && s.TenantId == TenantId && s.Status == IdempotencyStatus.Processing),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _storeMock.Setup(x => x.CompleteAsync(
                key,
                It.IsAny<string>(),
                IdempotencyStatus.Failed,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _behavior.Handle(
                command,
                _ => throw expectedException,
                CancellationToken.None));

        _storeMock.Verify(x => x.FailAsync(
            key,
            IdempotencyStatus.Failed,
            It.IsAny<CancellationToken>()),
            Times.Once);
            }

    [Fact]
    public async Task Handle_CancelledOperation_MarksAsCancelled()
    {
        // Arrange
        const string key = "test-id-cancelled";
        var command = new TestCommand { IdempotencyKey = key, Data = "test" };
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _storeMock.Setup(x => x.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyState?)null);

        _storeMock.Setup(x => x.CreateAsync(
                It.Is<IdempotencyState>(s => s.Key == key && s.Status == IdempotencyStatus.Processing),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _storeMock.Setup(x => x.CompleteAsync(
                key,
                It.IsAny<string>(),
                IdempotencyStatus.Cancelled,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _behavior.Handle(
                command,
                cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.FromResult(new TestResponse());
                },
                cts.Token));

        _storeMock.Verify(x => x.FailAsync(
            key,
            IdempotencyStatus.Cancelled,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}