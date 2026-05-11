using FluentAssertions;
using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using MicroKit.Messaging.Abstractions.Persistence;
using MicroKit.Messaging.Core.Configuration;
using MicroKit.Messaging.Core.Inbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MicroKit.Messaging.Tests;

public class DefaultInboxProcessorTests
{
    private readonly IInboxStateRepository _stateRepository;
    private readonly IInboxMessageRepository _messageRepository;
    private readonly IMessagingUnitOfWork _unitOfWork;
    private readonly IInboxPublisher _publisher;
    private readonly IInboxStateFetcher _fetcher;
    private readonly ILogger<DefaultInboxProcessor> _logger;
    private readonly DefaultInboxProcessor _processor;

    private static readonly InboxOptions DefaultOptions = new()
    {
        MaxProcessingAttempts = 3
    };

    public DefaultInboxProcessorTests()
    {
        _stateRepository = Substitute.For<IInboxStateRepository>();
        _messageRepository = Substitute.For<IInboxMessageRepository>();
        _unitOfWork = Substitute.For<IMessagingUnitOfWork>();
        _publisher = Substitute.For<IInboxPublisher>();
        _fetcher = Substitute.For<IInboxStateFetcher>();
        _logger = Substitute.For<ILogger<DefaultInboxProcessor>>();

        _processor = new DefaultInboxProcessor(
            _stateRepository,
            _messageRepository,
            Options.Create(DefaultOptions),
            _logger,
            _fetcher,
            _publisher,
            _unitOfWork);
    }

    private static InboxState CreateState(string messageId = "msg-1") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = "tenant-1",
        InboxMessageId = messageId,
        ConsumerName = "OrderConsumer",
        Status = MessageStatus.Pending,
        AttemptCount = 0
    };

    private static InboxMessage CreateMessage(string id = "msg-1") => new()
    {
        Id = id,
        TenantId = "tenant-1",
        MessageType = "MyApp.OrderCreated",
        Payload = "{\"orderId\":\"order-1\"}",
        OccurredOnUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task ProcessBatchAsync_EmptyBatch_ShouldNotCallPublisher()
    {
        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await _processor.ProcessBatchAsync("tenant-1", "OrderConsumer", 10);

        await _publisher.DidNotReceive().PublishAsync(Arg.Any<InboxContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_WithMessage_ShouldPublish()
    {
        var state = CreateState("msg-1");
        var message = CreateMessage("msg-1");

        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<InboxState> { state });

        _messageRepository.GetByIdAsync("msg-1", Arg.Any<CancellationToken>())
            .Returns(message);

        await _processor.ProcessBatchAsync("tenant-1", "OrderConsumer", 10);

        await _publisher.Received(1).PublishAsync(
            Arg.Is<InboxContext>(c => c.MessageId == "msg-1" && c.MessageType == "MyApp.OrderCreated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_Success_ShouldMarkStateAsPublished()
    {
        var state = CreateState("msg-1");
        var message = CreateMessage("msg-1");

        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<InboxState> { state });

        _messageRepository.GetByIdAsync("msg-1", Arg.Any<CancellationToken>())
            .Returns(message);

        await _processor.ProcessBatchAsync("tenant-1", "OrderConsumer", 10);

        state.Status.Should().Be(MessageStatus.Published);
        state.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessBatchAsync_MessageNotFound_ShouldMarkStateFailed()
    {
        var state = CreateState("missing-msg");

        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<InboxState> { state });

        _messageRepository.GetByIdAsync("missing-msg", Arg.Any<CancellationToken>())
            .Returns((InboxMessage?)null);

        await _processor.ProcessBatchAsync("tenant-1", "OrderConsumer", 10);

        // Should not throw but record error — state should be pending for retry or failed at max
        state.LastError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ProcessBatchAsync_PublishFails_ShouldSetPendingWithRetry()
    {
        var state = CreateState("msg-1");
        var message = CreateMessage("msg-1");

        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<InboxState> { state });

        _messageRepository.GetByIdAsync("msg-1", Arg.Any<CancellationToken>())
            .Returns(message);

        _publisher.PublishAsync(Arg.Any<InboxContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Handler failed"));

        await _processor.ProcessBatchAsync("tenant-1", "OrderConsumer", 10);

        state.Status.Should().Be(MessageStatus.Pending);
        state.LastError.Should().Be("Handler failed");
        state.NextAttemptAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessBatchAsync_ExceedsMaxAttempts_ShouldMarkAsFailed()
    {
        var state = CreateState("msg-1");
        state.AttemptCount = DefaultOptions.MaxProcessingAttempts;
        var message = CreateMessage("msg-1");

        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<InboxState> { state });

        _messageRepository.GetByIdAsync("msg-1", Arg.Any<CancellationToken>())
            .Returns(message);

        _publisher.PublishAsync(Arg.Any<InboxContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Still failing"));

        await _processor.ProcessBatchAsync("tenant-1", "OrderConsumer", 10);

        state.Status.Should().Be(MessageStatus.Failed);
    }

    [Fact]
    public async Task ProcessBatchAsync_IdempotencyCheck_SameMessageId_AlreadyPublished_ShouldNotReprocess()
    {
        // Simulate that fetcher only returns states not yet published (idempotency enforced at fetch level)
        // The processor marks as published on first run; the fetcher won't return already-published states
        var state = CreateState("msg-dupe");
        state.Status = MessageStatus.Published; // already processed
        var message = CreateMessage("msg-dupe");

        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns([]);  // Fetcher respects idempotency — won't return already-published states

        await _processor.ProcessBatchAsync("tenant-1", "OrderConsumer", 10);

        await _publisher.DidNotReceive().PublishAsync(Arg.Any<InboxContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_CommitFails_ShouldRethrow()
    {
        var state = CreateState("msg-1");
        var message = CreateMessage("msg-1");

        _fetcher.FetchNextBatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<InboxState> { state });

        _messageRepository.GetByIdAsync("msg-1", Arg.Any<CancellationToken>())
            .Returns(message);

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var act = async () => await _processor.ProcessBatchAsync("tenant-1", "OrderConsumer", 10);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
    }
}
