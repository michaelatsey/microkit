using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;
using MicroKit.Messaging.IntegrationTests.PostgreSql;
using MicroKit.Messaging.IntegrationTests.PostgreSql.Instances;

namespace MicroKit.Messaging.IntegrationTests.Hosting;

/// <summary>
/// V11 (#215): a host built as the README shows a store with a transport, under the Development
/// defaults — <c>ValidateOnBuild</c> and <c>ValidateScopes</c> on — starts, and a commit wakes its
/// store's outbox loop: the commit signal's registration holds under both validations.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class CommitWakeHostShapeTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    [DockerRequiredFact]
    public async Task AStoreWithATransport_UnderDevelopmentValidation_StartsAndACommitWakesItsLoop()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            ApplicationName = "tests.commit-wake-host-shape",
        });
        builder.Services.AddSingleton<WakeRecorder>();
        builder.Services.AddDbContext<TestMessagingDbContext>(o => o.UseNpgsql(db.ConnectionString));
        builder.Services.AddMicroKitMessaging(bus =>
        {
            bus.Publishes<WakeEvent>();
            bus.AddHandler<WakeEventRecorder>();
            bus.UseEntityFrameworkOutbox<TestMessagingDbContext>();
            bus.UseInMemoryTransport();
            bus.Configure(o =>
            {
                o.Outbox.PollingInterval = TimeSpan.FromSeconds(1);
                o.Outbox.MaxPollingInterval = TimeSpan.FromMinutes(10);
            });
        });

        // The probe: each claim on a timeline, so the test commits once the loop is in its long wait.
        builder.Services.AddSingleton<ClaimTimeline>();
        builder.Services.AddKeyedScoped<IOutboxProcessorStore>(WakeHost.Store, static (sp, _) => new TimelineOutboxStore(
            sp.GetRequiredService<EfOutboxStore<TestMessagingDbContext>>(),
            WakeHost.Database,
            sp.GetRequiredService<ClaimTimeline>(),
            TimelineOutboxStore.WakeOf(sp, WakeHost.Store)));

        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var claims = host.Services.GetRequiredService<ClaimTimeline>();
            await Eventually.HoldsAsync(() => claims.Completed(WakeHost.Database) >= 3, "three empty claims: the loop waits 8 s");

            var id = Guid.NewGuid();
            await using (var scope = host.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new WakeEvent(id));
                await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
            }

            var committed = Stopwatch.GetTimestamp();
            var recorder = host.Services.GetRequiredService<WakeRecorder>();
            await Eventually.HoldsAsync(() => recorder.Delivered.ContainsKey(id), "the handler runs");
            var latency = Stopwatch.GetElapsedTime(committed, recorder.Delivered[id]);
            output.WriteLine($"wake-latency v11 ms={latency.TotalMilliseconds:F1}");
            latency.ShouldBeLessThan(CommitWakeTests.Bound, "the commit woke the loop: the row did not wait the 8 s idle delay");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}

public sealed class WakeEventRecorder(WakeRecorder recorder) : IMessageHandler<WakeEvent>
{
    public ValueTask HandleAsync(WakeEvent message, MessageContext context, CancellationToken ct = default)
    {
        recorder.Delivered.TryAdd(message.Id, Stopwatch.GetTimestamp());
        return ValueTask.CompletedTask;
    }
}
