using FluentAssertions;
using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Abstractions.Persistence;
using MicroKit.Messaging.Core.Configuration;
using MicroKit.Messaging.Core.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MicroKit.Messaging.Tests;

public class DefaultOutboxProcessorTests
{
    private readonly IOutboxRepository _repository;
    private readonly IOutboxPublisher _publisher;
    private readonly IMessagingUnitOfWork _unitOfWork;
    private readonly ILogger<DefaultOutboxProcessor> _logger;
    private readonly IOutboxMessageFetcher _fetcher;
    private readonly DefaultOutboxProcessor _processor;

    private static readonly OutboxOptions DefaultOptions = new()
    {
        MaxRetryCount = 3,
        LockDurationInMinutes = TimeSpan.FromMinutes(5)
    };

    public DefaultOutboxProcessorTests()
    {
        _repository = Substitute.For<IOutboxRepository>();
        _publisher = Substitute.For<IOutboxPublisher>();
        _unitOfWork = Substitute.For<IMessagingUnitOfWork>();
        _logger = Substitute.For<ILogger<DefaultOutboxProcessor>>();
        _fetcher = Substitute.For<IOutboxMessageFetcher>();

        _processor = new DefaultOutboxProcessor(
            _repository,
            _publisher,
            Options.Create(DefaultOptions),
            _logger,
            _fetcher,
            _unitOfWork);
    }

    private static OutboxMessage CreateMessage(string id = "msg-1") => new()
    {
        Id = id,
        TenantId = "tenant-1",
        MessageType = "MyApp.OrderCreated",
        Payload = "{}",
        PublishAsNotification = true,
        Status = MessageStatus.Pending,
        RetryCount = 0
    };

    [Fact]
    public async Task ProcessBatchAsync_EmptyBatch_ShouldNotCallPublisher()
    {
        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await _processor.ProcessBatchAsync("tenant-1", 10);

        await _publisher.DidNotReceive().PublishAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_SingleMessage_ShouldPublishAndMarkPublished()
    {
        var message = CreateMessage();
        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        await _processor.ProcessBatchAsync("tenant-1", 10);

        await _publisher.Received(1).PublishAsync(message, Arg.Any<CancellationToken>());
        message.Status.Should().Be(MessageStatus.Published);
        message.ProcessedAtUtc.Should().NotBeNull();
        message.Error.Should().BeNull();
    }

    [Fact]
    public async Task ProcessBatchAsync_SuccessfulBatch_ShouldSaveChanges()
    {
        var message = CreateMessage();
        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        await _processor.ProcessBatchAsync("tenant-1", 10);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_PublishFails_ShouldMarkAsPendingWithBackoff()
    {
        var message = CreateMessage();
        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        _publisher.PublishAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Transport unavailable"));

        await _processor.ProcessBatchAsync("tenant-1", 10);

        message.Status.Should().Be(MessageStatus.Pending);
        message.Error.Should().Be("Transport unavailable");
        message.ScheduledAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessBatchAsync_ExceedsMaxRetries_ShouldMarkAsFailed()
    {
        var message = CreateMessage();
        message.RetryCount = DefaultOptions.MaxRetryCount; // already at max
        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        _publisher.PublishAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Still failing"));

        await _processor.ProcessBatchAsync("tenant-1", 10);

        message.Status.Should().Be(MessageStatus.Failed);
    }

    [Fact]
    public async Task ProcessBatchAsync_CommitFails_ShouldRethrow()
    {
        var message = CreateMessage();
        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB commit failed"));

        var act = async () => await _processor.ProcessBatchAsync("tenant-1", 10);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB commit failed");
    }

    [Fact]
    public async Task ProcessBatchAsync_MultipleBatch_ShouldPublishAllMessages()
    {
        var messages = Enumerable.Range(1, 5)
            .Select(i => CreateMessage($"msg-{i}"))
            .ToList();

        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(messages);

        await _processor.ProcessBatchAsync("tenant-1", 5);

        await _publisher.Received(5).PublishAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }
}
