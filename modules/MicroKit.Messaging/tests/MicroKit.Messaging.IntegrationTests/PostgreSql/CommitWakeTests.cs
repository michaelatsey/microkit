using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using MicroKit.Messaging.IntegrationTests.PostgreSql.Instances;

namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// A commit wakes its single store's outbox loop (#215), on PostgreSQL: the EF store signals at the end
/// of the scope, once a row it enlisted is saved and its guarded attempt, if any, committed (D1).
/// </summary>
/// <remarks>
/// <para>
/// The loops run on the real clock (#213). A latency test pairs a short polling interval — the floor
/// (D6b) bounds a wake from below by it — with a long idle wait: polling 1 s, ceiling 10 min, so after
/// its third empty claim the loop waits 8 s. Each test waits for that claim to have returned before it
/// commits, so no claim already under way can pick its row up.
/// </para>
/// <para>
/// A "no wake" test waits for the next claim and asserts it was the idle one, to
/// <see cref="Tolerance"/>: a wake would have started it at the floor, a fraction of the idle wait.
/// </para>
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class CommitWakeTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private static readonly TimeSpan Polling = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MaxPolling = TimeSpan.FromMinutes(10);

    // The "no wake" tests: polling 250 ms, so the wait after the third empty claim is 2 s.
    private static readonly TimeSpan ShortPolling = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ShortIdleWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The latency bound of a woken publication, from the end of its scope to its send or delivery.
    /// Measured over 5 runs (2026-11-24, WSL2): V1 980–998 ms, V2 986–1010 ms, V7 968–984 ms, V11 978–1036 ms —
    /// the floor's remainder (the commit comes right after a claim) plus at most 36 ms; V3, whose commit
    /// comes after the floor has passed, 3.9–6.2 ms. Bound = polling interval + 500 ms: 14x the overhead
    /// measured beyond the floor, and under a fifth of the 8 s idle wait a missed wake costs.
    /// </summary>
    internal static readonly TimeSpan Bound = Polling + TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How far a real-time wait may end short of itself against the Stopwatch: max(10 ms, 5% of the
    /// wait), the rule of the unit suite's <c>OutboxWakeTests.Tolerance</c>, where it is measured.
    /// </summary>
    private static TimeSpan Tolerance(TimeSpan wait)
    {
        var proportional = wait * 0.05;
        var least = TimeSpan.FromMilliseconds(10);
        return proportional > least ? proportional : least;
    }

    // ---------------------------------------------------------------------------
    // V1–V3: a commit wakes the loop
    // ---------------------------------------------------------------------------

    /// <summary>V1: a publication committed by SaveChanges leaves within the bound, far below the idle wait.</summary>
    [DockerRequiredFact]
    public async Task APublicationCommittedBySaveChanges_OnAnIdleStore_IsPublishedWithinTheBound()
    {
        await using var host = await WakeHost.StartAsync(fixture.ConnectionString, Polling, MaxPolling);
        await host.ClaimedAsync(3, "three empty claims: the loop waits 8 s");

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            id = (await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new WakeEvent(Guid.NewGuid()))).Value;
            await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
        }

        var committed = Stopwatch.GetTimestamp();
        await Eventually.HoldsAsync(() => host.Recorder.Sent.ContainsKey(id), "the publication is sent");
        var latency = Stopwatch.GetElapsedTime(committed, host.Recorder.Sent[id]);
        output.WriteLine($"wake-latency v1 ms={latency.TotalMilliseconds:F1}");
        latency.ShouldBeLessThan(Bound, "the commit woke the loop: the row did not wait the 8 s idle delay");
    }

    /// <summary>V2: a domain event's local handler — a local row of the same loop — runs within the bound.</summary>
    [DockerRequiredFact]
    public async Task ALocalMessageCommittedBySaveChanges_OnAnIdleStore_IsDeliveredWithinTheBound()
    {
        await using var host = await WakeHost.StartAsync(fixture.ConnectionString, Polling, MaxPolling);
        await host.ClaimedAsync(3, "three empty claims: the loop waits 8 s");

        var id = Guid.NewGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILocalMessageSender>()
                .SendAsync(new WakeNotice(id), MessageId.From(id), DateTimeOffset.UtcNow);
            await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
        }

        var committed = Stopwatch.GetTimestamp();
        await Eventually.HoldsAsync(() => host.Recorder.Delivered.ContainsKey(id), "the local message is delivered");
        var latency = Stopwatch.GetElapsedTime(committed, host.Recorder.Delivered[id]);
        output.WriteLine($"wake-latency v2 ms={latency.TotalMilliseconds:F1}");
        latency.ShouldBeLessThan(Bound, "the commit woke the loop: the local handler did not wait the 8 s idle delay");
    }

    /// <summary>
    /// V3: under an explicit transaction, the wake comes after the commit. The pause between the save
    /// and the commit outlasts the floor: a wake raised before the commit would be spent on a claim that
    /// sees nothing, and the row would wait the next, longer, idle delay.
    /// </summary>
    [DockerRequiredFact]
    public async Task APublicationUnderAnExplicitTransaction_IsPublishedWithinTheBoundAfterItsCommit()
    {
        await using var host = await WakeHost.StartAsync(fixture.ConnectionString, Polling, MaxPolling);
        await host.ClaimedAsync(3, "three empty claims: the loop waits 8 s");

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            id = (await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new WakeEvent(Guid.NewGuid()))).Value;
            await db.SaveChangesAsync();
            await Task.Delay(Polling + TimeSpan.FromMilliseconds(500));
            await transaction.CommitAsync();
        }

        var committed = Stopwatch.GetTimestamp();
        await Eventually.HoldsAsync(() => host.Recorder.Sent.ContainsKey(id), "the publication is sent");
        var latency = Stopwatch.GetElapsedTime(committed, host.Recorder.Sent[id]);
        output.WriteLine($"wake-latency v3 ms={latency.TotalMilliseconds:F1}");
        latency.ShouldBeLessThan(Bound, "the wake came after the commit: the row did not wait the next idle delay");
    }

    // ---------------------------------------------------------------------------
    // V4b: the floor during a transport outage
    // ---------------------------------------------------------------------------

    /// <summary>
    /// V4b: with the transport down and commits arriving ten times per polling interval, consecutive
    /// claims start no sooner than the polling interval apart, to the timer's precision. No batch
    /// saturates: a saturated batch polls again at once by design.
    /// </summary>
    [DockerRequiredFact]
    public async Task DuringATransportOutage_CommitsFasterThanThePollingInterval_SpaceClaimsByAtLeastIt()
    {
        await using var host = await WakeHost.StartAsync(fixture.ConnectionString, Polling, MaxPolling, o =>
            o.Outbox.TransportUnavailableBackoff = TimeSpan.FromSeconds(2));
        host.Recorder.TransportDown = true;
        await host.ClaimedAsync(1, "the loop claimed once");

        var bus = Stopwatch.StartNew();
        var commits = 0;
        while (bus.Elapsed < TimeSpan.FromSeconds(8))
        {
            await using (var scope = host.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new WakeEvent(Guid.NewGuid()));
                await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();
            }

            commits++;
            await Task.Delay(100);
        }

        var claims = host.Claims.Of(WakeHost.Database);
        var gaps = new List<TimeSpan>();
        for (var i = 1; i < claims.Length; i++)
        {
            gaps.Add(Stopwatch.GetElapsedTime(claims[i - 1].ClaimStarted, claims[i].ClaimStarted));
        }

        // After an empty claim, idle polling waits at least twice the polling interval: a claim sooner
        // than 1.5 intervals after an empty one was woken. Measured over 5 runs (2026-11-24, WSL2): 9 claims,
        // 5 woken after an empty one, at most 55 rows claimed, gaps 996.1–1007.2 ms — a shortfall of at most
        // 3.9 ms against the 50 ms tolerance of a 1 s floor.
        var woken = 0;
        for (var i = 1; i < claims.Length; i++)
        {
            if (claims[i - 1].Claimed == 0 && gaps[i - 1] < Polling * 1.5)
            {
                woken++;
            }
        }

        output.WriteLine($"v4b commits={commits} claims={claims.Length} woken-after-empty={woken} max-claimed={claims.Max(c => c.Claimed)} " +
            $"claimed={string.Join(' ', claims.Select(c => c.Claimed))} " +
            $"gaps-ms={string.Join(' ', gaps.Select(g => g.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))}");

        claims.ShouldAllBe(c => c.Claimed < 1000, "no batch saturated: each claimed below the batch size");
        woken.ShouldBeGreaterThan(0, "control: the commits woke the loop — after an empty claim, idle polling waits two intervals");
        gaps.ShouldAllBe(g => g >= Polling - Tolerance(Polling), "a wake starts a claim no sooner than the polling interval after the previous one");
    }

    // ---------------------------------------------------------------------------
    // V8: no wake without a saved, committed row
    // ---------------------------------------------------------------------------

    /// <summary>V8: a row its scope never saved stays Added: no wake.</summary>
    [DockerRequiredFact]
    public async Task ARowNeverSaved_WakesNothing()
    {
        await using var host = await WakeHost.StartAsync(fixture.ConnectionString, ShortPolling, MaxPolling);
        await host.ClaimedAsync(3, "three empty claims: the loop waits 2 s");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new WakeEvent(Guid.NewGuid()));
        }

        await NextClaimIsTheIdleOneAsync(host, "v8-added");
    }

    /// <summary>V8: a row detached by a clear of the tracker: no wake.</summary>
    [DockerRequiredFact]
    public async Task ARowDetachedByAClearOfTheTracker_WakesNothing()
    {
        await using var host = await WakeHost.StartAsync(fixture.ConnectionString, ShortPolling, MaxPolling);
        await host.ClaimedAsync(3, "three empty claims: the loop waits 2 s");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new WakeEvent(Guid.NewGuid()));
            scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().ChangeTracker.Clear();
        }

        await NextClaimIsTheIdleOneAsync(host, "v8-detached");
    }

    /// <summary>
    /// A row saved, then cleared from the tracker, reads as discarded when the scope ends (a documented
    /// limit, #215): one Debug 4001, never 4000, no wake — and, its save committed, it is published at
    /// the next poll.
    /// </summary>
    [DockerRequiredFact]
    public async Task ARowSavedThenClearedFromTheTracker_IsReportedAtDebug_WakesNothing_AndIsPublishedAtTheNextPoll()
    {
        await using var host = await WakeHost.StartAsync(fixture.ConnectionString, ShortPolling, MaxPolling);
        await host.ClaimedAsync(3, "three empty claims: the loop waits 2 s");
        var mark = host.Logs.Mark();

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>();
            id = (await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new WakeEvent(Guid.NewGuid()))).Value;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        var logged = host.Logs.Since(mark);
        logged.Where(e => e.EventId == 4001).ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Debug);
        logged.ShouldNotContain(e => e.EventId == 4000, "the row was saved: it is not lost");

        await NextClaimIsTheIdleOneAsync(host, "v8-saved-then-cleared");
        await Eventually.HoldsAsync(() => host.Recorder.Sent.ContainsKey(id), "its save committed: the next poll publishes it");
    }

    /// <summary>
    /// V8: a guarded handler that saves its publication inside its attempt, then throws. The row is
    /// Unchanged when the scope ends, and the attempt uncommitted: it rolled back, so no wake.
    /// </summary>
    [DockerRequiredFact]
    public async Task ARowSavedInAGuardedAttemptThatDidNotCommit_WakesNothing()
    {
        await using var host = await WakeHost.StartAsync(fixture.ConnectionString, ShortPolling, MaxPolling);
        await host.ClaimedAsync(3, "three empty claims: the loop waits 2 s");

        var message = Guid.NewGuid();
        var result = await host.Services.GetRequiredService<IEnvelopeReceiver>().ReceiveAsync(new MessageEnvelope
        {
            Id = message.ToString(),
            Source = "tests.commit-wake.upstream",
            Type = SaveThenThrow.WireType,
            Time = DateTimeOffset.UtcNow,
            DataContentType = "application/json",
            Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new SaveThenThrow(message), JsonSerializerOptions.Web)),
        });

        result.Disposition.ShouldBe(ReceiveDisposition.Acknowledge, "control: the failure was recorded on the handler's row");
        await using (var db = new TestMessagingDbContext(new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(host.ConnectionString).Options))
        {
            (await db.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(0, "control: the saved publication rolled back with its attempt");
            (await db.InboxMessages.AsNoTracking().CountAsync(r => r.DeadLettered)).ShouldBe(1, "control: the handler ran and failed");
        }

        await NextClaimIsTheIdleOneAsync(host, "v8-uncommitted-attempt");
    }

    // ---------------------------------------------------------------------------
    // V9: a signal after host stop is a no-op
    // ---------------------------------------------------------------------------

    /// <summary>V9: a scope that saved a row and ends after host stop disposes without an exception.</summary>
    [DockerRequiredFact]
    public async Task AScopeThatSavedARow_EndingAfterHostStop_DisposesWithoutAnException()
    {
        await using var host = await WakeHost.StartAsync(fixture.ConnectionString, ShortPolling, MaxPolling);
        var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new WakeEvent(Guid.NewGuid()));
        await scope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().SaveChangesAsync();

        await host.StopAsync();

        await Should.NotThrowAsync(async () => await scope.DisposeAsync());
    }

    private async Task NextClaimIsTheIdleOneAsync(WakeHost host, string name)
    {
        await host.ClaimedAsync(4, "the loop claimed again");
        var claims = host.Claims.Of(WakeHost.Database);
        var gap = claims[2].Until(claims[3]);
        output.WriteLine($"{name} next-claim-after-ms={gap.TotalMilliseconds:F1} idle-ms={ShortIdleWait.TotalMilliseconds}");
        gap.ShouldBeGreaterThanOrEqualTo(ShortIdleWait - Tolerance(ShortIdleWait),
            "no wake: the next claim came at the end of the 2 s idle wait, not at the floor");
    }
}
