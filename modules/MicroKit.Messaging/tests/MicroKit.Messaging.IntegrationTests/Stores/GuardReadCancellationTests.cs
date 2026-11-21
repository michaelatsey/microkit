using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace MicroKit.Messaging.IntegrationTests.Stores;

/// <summary>
/// A host stop while the guard reads its row: the provider's cancellation carries a transient fault
/// inside it. It is the stop it is — the delivery unsettled, nothing charged, nothing written (D10) —
/// never an outage (#188, review of 556bbc0).
/// </summary>
public sealed class GuardReadCancellationTests
{
    [Fact]
    public Task AHostStopDuringTheGuardsRead_IsUnsettled_NeverAnOutage()
        => Task.Run(async () =>
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            using var stopping = new CancellationTokenSource();
            var stopDuringRead = new StopDuringInboxRead(stopping);
            var logs = new CapturingLoggerProvider();

            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(logs));
            services.AddDbContext<TestMessagingDbContext>(o => o.UseSqlite(connection).AddInterceptors(stopDuringRead));
            services.AddMicroKitMessaging(bus =>
            {
                bus.ServiceName = "tests.guard-stop";
                bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
                bus.AddHandler<StopHandler>();
            });
            await using var provider = services.BuildServiceProvider();
            await using (var schema = provider.CreateAsyncScope())
            {
                await schema.ServiceProvider.GetRequiredService<TestMessagingDbContext>().Database.EnsureCreatedAsync();
            }

            stopDuringRead.Armed = true;
            var result = await provider.GetRequiredService<IEnvelopeReceiver>().ReceiveAsync(Envelope(), stopping.Token);

            stopDuringRead.Fired.ShouldBeTrue();
            result.Disposition.ShouldBe(ReceiveDisposition.Unsettled, "a host stop is never a failure or an outage");
            logs.Entries.ShouldNotContain(e => e.EventId == 2209, "the store was never down");
            StopHandler.Runs.ShouldBe(0);

            await using var read = provider.CreateAsyncScope();
            (await read.ServiceProvider.GetRequiredService<TestMessagingDbContext>().InboxMessages.AsNoTracking().CountAsync())
                .ShouldBe(0, "nothing charged, nothing written");
        });

    private static MessageEnvelope Envelope() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Source = "tests.guard-stop",
        Type = "tests.guard-stop.event.v1",
        Time = DateTimeOffset.UtcNow,
        DataContentType = "application/json",
        Headers = new Dictionary<string, string>(StringComparer.Ordinal),
        Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new StopEvent(Guid.NewGuid()), JsonSerializerOptions.Web)),
    };

    [MessageContract("tests.guard-stop.event")]
    private sealed record StopEvent(Guid Id) : IIntegrationEvent;

    private sealed class StopHandler : IMessageHandler<StopEvent>
    {
        private static int _runs;

        public static int Runs => Volatile.Read(ref _runs);

        public ValueTask HandleAsync(StopEvent message, MessageContext context, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _runs);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>The host stops while the guard reads: a cancellation wrapping a transient provider fault.</summary>
    private sealed class StopDuringInboxRead(CancellationTokenSource stopping) : DbCommandInterceptor
    {
        private int _fired;

        public bool Armed { get; set; }

        public bool Fired => Volatile.Read(ref _fired) == 1;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("\"InboxMessages\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await stopping.CancelAsync();
                throw new OperationCanceledException("the host stopped mid-read", new InjectedTransientReadException(), stopping.Token);
            }

            return result;
        }
    }
}
