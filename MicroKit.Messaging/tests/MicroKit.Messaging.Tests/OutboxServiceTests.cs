using FluentAssertions;
using MicroKit.Abstractions.Serialization;
using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Core.Outbox;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;

namespace MicroKit.Messaging.Tests;

public class OutboxServiceTests
{
    private readonly IOutboxRepository _repository;
    private readonly ILogger<OutboxService> _logger;
    private readonly IMicroKitSerializer _serializer;
    private readonly OutboxService _service;

    private static readonly OutboxDestination NotificationDestination = new()
    {
        PublishAsNotification = true,
        PublishToBroker = false
    };

    private static readonly OutboxDestination BrokerDestination = new()
    {
        PublishAsNotification = false,
        PublishToBroker = true,
        BrokerTopic = "orders.events"
    };

    public OutboxServiceTests()
    {
        _repository = Substitute.For<IOutboxRepository>();
        _logger = Substitute.For<ILogger<OutboxService>>();
        _serializer = Substitute.For<IMicroKitSerializer>();

        _serializer.Serialize(Arg.Any<object>()).Returns(call =>
            JsonSerializer.Serialize(call.Arg<object>()));

        _service = new OutboxService(_repository, _logger, _serializer);
    }

    [Fact]
    public async Task EnqueueAsync_Generic_ShouldCallRepository_AddAsync()
    {
        _repository.AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .Returns("msg-1");

        var messageId = await _service.EnqueueAsync(
            tenantId: "tenant-1",
            messageId: "msg-1",
            payload: new { Name = "Order" },
            destination: NotificationDestination);

        await _repository.Received(1).AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        messageId.Should().Be("msg-1");
    }

    [Fact]
    public async Task EnqueueAsync_Generic_ShouldSetCorrectFields()
    {
        OutboxMessage? captured = null;
        await _repository.AddAsync(Arg.Do<OutboxMessage>(m => captured = m), Arg.Any<CancellationToken>());

        await _service.EnqueueAsync(
            tenantId: "tenant-99",
            messageId: "msg-abc",
            payload: new { Val = 1 },
            destination: NotificationDestination,
            correlationId: "corr-1",
            causationId: "cause-1",
            idempotencyKey: "idem-1");

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be("tenant-99");
        captured.Id.Should().Be("msg-abc");
        captured.CorrelationId.Should().Be("corr-1");
        captured.CausationId.Should().Be("cause-1");
        captured.IdempotencyKey.Should().Be("idem-1");
        captured.Status.Should().Be(MessageStatus.Pending);
        captured.PublishAsNotification.Should().BeTrue();
    }

    [Fact]
    public async Task EnqueueAsync_Raw_ShouldCallRepository_AddAsync()
    {
        _repository.AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .Returns("msg-2");

        var messageId = await _service.EnqueueAsync(
            messageId: "msg-2",
            tenantId: "tenant-1",
            messageType: "MyApp.OrderCreated",
            payload: "{\"id\":1}",
            destination: BrokerDestination);

        await _repository.Received(1).AddAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        messageId.Should().Be("msg-2");
    }

    [Fact]
    public async Task EnqueueAsync_WithBrokerDestination_ShouldSetBrokerTopic()
    {
        OutboxMessage? captured = null;
        await _repository.AddAsync(Arg.Do<OutboxMessage>(m => captured = m), Arg.Any<CancellationToken>());

        await _service.EnqueueAsync(
            messageId: "m",
            tenantId: "t",
            messageType: "T",
            payload: "{\"x\":1}",
            destination: BrokerDestination);

        captured!.PublishToBroker.Should().BeTrue();
        captured.BrokerTopic.Should().Be("orders.events");
    }

    [Fact]
    public async Task EnqueueAsync_EmptyPayload_ShouldThrow_ArgumentException()
    {
        var act = async () => await _service.EnqueueAsync(
            messageId: "m",
            tenantId: "t",
            messageType: "T",
            payload: "",
            destination: NotificationDestination);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EnqueueAsync_NoDestination_ShouldThrow_ArgumentException()
    {
        var noDestination = new OutboxDestination
        {
            PublishAsNotification = false,
            PublishToBroker = false
        };

        var act = async () => await _service.EnqueueAsync(
            messageId: "m",
            tenantId: "t",
            messageType: "T",
            payload: "{\"x\":1}",
            destination: noDestination);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EnqueueAsync_BrokerWithoutTopic_ShouldThrow_ArgumentException()
    {
        var brokerNoTopic = new OutboxDestination
        {
            PublishAsNotification = false,
            PublishToBroker = true,
            BrokerTopic = null
        };

        var act = async () => await _service.EnqueueAsync(
            messageId: "m",
            tenantId: "t",
            messageType: "T",
            payload: "{\"x\":1}",
            destination: brokerNoTopic);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EnqueueBatchAsync_ShouldCallAddRangeAsync()
    {
        var messages = new List<OutboxMessage>
        {
            new() { MessageType = "T", Payload = "{}", PublishAsNotification = true },
            new() { MessageType = "T", Payload = "{}", PublishAsNotification = true }
        };

        var ids = await _service.EnqueueBatchAsync(messages);

        await _repository.Received(1).AddRangeAsync(
            Arg.Any<IReadOnlyCollection<OutboxMessage>>(),
            Arg.Any<CancellationToken>());

        ids.Should().HaveCount(2);
    }

    [Fact]
    public async Task EnqueueBatchAsync_EmptyList_ShouldNotCallRepository()
    {
        var ids = await _service.EnqueueBatchAsync([]);

        await _repository.DidNotReceive().AddRangeAsync(
            Arg.Any<IReadOnlyCollection<OutboxMessage>>(),
            Arg.Any<CancellationToken>());

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task EnqueueBatchAsync_ShouldAssignIds_WhenMissing()
    {
        IReadOnlyCollection<OutboxMessage>? captured = null;
        await _repository.AddRangeAsync(
            Arg.Do<IReadOnlyCollection<OutboxMessage>>(m => captured = m),
            Arg.Any<CancellationToken>());

        var messages = new List<OutboxMessage>
        {
            new() { MessageType = "T", Payload = "{}", PublishAsNotification = true },  // no Id
        };

        await _service.EnqueueBatchAsync(messages);

        captured.Should().NotBeNull();
        captured!.All(m => !string.IsNullOrEmpty(m.Id)).Should().BeTrue();
    }
}
