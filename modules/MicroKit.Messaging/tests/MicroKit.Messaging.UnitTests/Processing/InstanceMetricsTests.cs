using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// V7 (#191 D9, D5, A2): a partitioned store's instruments. With the instance tag off, one series per
/// (store.key, endpoint), its back-off counters summed over the instances — zero with none; with it
/// on, one series per instance; a released instance's state is gone and its series no longer report. A
/// single store's series are unchanged.
/// </summary>
public sealed class InstanceMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly MessageStoreKey Single = new("tests:single");
    private const string Endpoints = "microkit.outbox.endpoints.backoff";
    private const string OutboxTenants = "microkit.outbox.tenants.backoff";
    private const string InboxTenants = "microkit.inbox.tenants.backoff";
    private const string Consumer = "Tests.InstanceConsumer, Tests";
    private const string OtherConsumer = "Tests.OtherInstanceConsumer, Tests";
    private const string InboxConsumers = "microkit.inbox.consumers.backoff";

    [Fact]
    public void WithTheTagOff_APartitionedStoreReportsOneSeriesPerEndpoint_SummedOverItsInstances_AndZeroWithNone()
    {
        using var harness = new InstanceHarness(tagInstances: false);
        var clock = new FakeTimeProvider(Now);
        using var outbox = new OutboxMetrics(clock, harness.OutboxOptions, harness.InstanceOptions, harness.Registry);
        using var inbox = new InboxMetrics(harness.Inbox, clock, harness.InboxOptions, harness.InstanceOptions, harness.Registry);
        using var recorder = new MeterRecorder(outbox.Meter, inbox.Meter);

        Observe(recorder, Endpoints).ShouldBe(
            [$"{InstanceHarness.Store}|-|local=0", $"{InstanceHarness.Store}|-|transport=0"],
            "a configured partitioned store reports zero while it has no instance, so an alert resolves");

        var a = Track(harness, outbox, "a");
        var b = Track(harness, outbox, "b");
        a.Outbox.Block(OutboxDispatchers.Transport, Now);
        b.Outbox.Block(OutboxDispatchers.Transport, Now);
        a.Outbox.TenantsOf(OutboxDispatchers.Local).OnDown("t1", Now, out _);
        a.Inbox.OnDown(Consumer, "t1", Now, out _);
        b.Inbox.OnDown(Consumer, "t2", Now, out _);

        Observe(recorder, Endpoints).ShouldBe(
            [$"{InstanceHarness.Store}|-|local=0", $"{InstanceHarness.Store}|-|transport=2"],
            "one series per (store.key, endpoint): the two instances' transport outages add up");
        Observe(recorder, OutboxTenants).ShouldBe(
            [$"{InstanceHarness.Store}|-|local=1", $"{InstanceHarness.Store}|-|transport=0"]);
        Observe(recorder, InboxTenants).ShouldBe([$"{InstanceHarness.Store}|-|=2"]);
    }

    [Fact]
    public void WithTheTagOn_EachInstanceReports_AndAReleasedInstanceNoLonger()
    {
        using var harness = new InstanceHarness(tagInstances: true);
        var clock = new FakeTimeProvider(Now);
        using var outbox = new OutboxMetrics(clock, harness.OutboxOptions, harness.InstanceOptions, harness.Registry);
        using var inbox = new InboxMetrics(harness.Inbox, clock, harness.InboxOptions, harness.InstanceOptions, harness.Registry);
        using var recorder = new MeterRecorder(outbox.Meter, inbox.Meter);

        var a = Track(harness, outbox, "a");
        Track(harness, outbox, "b");
        a.Outbox.Block(OutboxDispatchers.Transport, Now);
        a.Inbox.OnDown(Consumer, "t1", Now, out _);

        Observe(recorder, Endpoints).ShouldBe(
        [
            $"{InstanceHarness.Store}|a|local=0", $"{InstanceHarness.Store}|a|transport=1",
            $"{InstanceHarness.Store}|b|local=0", $"{InstanceHarness.Store}|b|transport=0",
        ]);
        Observe(recorder, InboxTenants).ShouldBe([$"{InstanceHarness.Store}|a|=1", $"{InstanceHarness.Store}|b|=0"]);
        harness.Inbox.IsQuiet.ShouldBeFalse("control: instance a's inbox is in back-off");

        // Released once its loops have ended (D5): its state is gone, its series no longer report, and a
        // loud state no longer holds the inbox's quiet flag.
        outbox.Untrack(a.Key);
        harness.Inbox.Release(a.Key);

        harness.Inbox.Stores.ShouldNotContain(e => e.Key == a.Key);
        harness.Inbox.IsQuiet.ShouldBeTrue("the released state left the group it counted in");
        Observe(recorder, Endpoints).ShouldBe([$"{InstanceHarness.Store}|b|local=0", $"{InstanceHarness.Store}|b|transport=0"]);
        Observe(recorder, InboxTenants).ShouldBe([$"{InstanceHarness.Store}|b|=0"]);

        // A delivery that held the released state writes into a detached object: nothing reads it.
        a.Inbox.OnDown(Consumer, "t2", Now, out _);
        harness.Inbox.IsQuiet.ShouldBeTrue();
        Observe(recorder, InboxTenants).ShouldBe([$"{InstanceHarness.Store}|b|=0"]);
    }

    [Fact]
    public void ASingleStore_KeepsItsSeries_WithNoInstanceTag_WhateverTheOption()
    {
        using var harness = new InstanceHarness(tagInstances: true);
        var clock = new FakeTimeProvider(Now);
        using var outbox = new OutboxMetrics(clock, harness.OutboxOptions, harness.InstanceOptions, harness.Registry);
        using var recorder = new MeterRecorder(outbox.Meter);
        var backoff = new OutboxEndpointBackoff(harness.OutboxOptions.PollingInterval, harness.OutboxOptions.TransportUnavailableBackoff);
        outbox.Track(Single, backoff);
        backoff.Block(OutboxDispatchers.Local, Now);

        var single = recorder.Observe(Endpoints).Where(r => r.Tag("store.key") == Single.Value).ToList();
        single.Select(r => r.Tags.Keys.Order(StringComparer.Ordinal).ToArray()).ShouldAllBe(
            keys => keys.SequenceEqual(new[] { "endpoint", "store.key" }), "the tags of a single store, as on main");

        outbox.RecordSettled(Single.Value, default);
        outbox.RecordSettled(Single.Value, Tally(), instance: null);
        recorder.Of("microkit.outbox.messages.settled").ShouldAllBe(r => !r.Tags.ContainsKey("store.instance"));
    }

    [Fact]
    public void Add_OfAnInstanceStillHeld_IsRefused_AndASingleStoresKeyIsNeverAnInstances()
    {
        using var harness = new InstanceHarness();
        var key = new StoreInstanceKey(InstanceHarness.Store, "a");
        harness.Inbox.Add(key);

        Should.Throw<InvalidOperationException>(() => harness.Inbox.Add(key), "an instance is released before it is drained again");
        Should.Throw<ArgumentNullException>(() => harness.Inbox.Add(new StoreInstanceKey(InstanceHarness.Store, null)));
        Should.Throw<ArgumentNullException>(() => harness.Metrics.Track(
            new StoreInstanceKey(InstanceHarness.Store, null), new OutboxEndpointBackoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))));
    }

    /// <summary>
    /// V11 and V12 (#210): a consumer held consumer-wide on instance a is not held on instance b — each instance
    /// has its own inbox back-off — and the consumers counter reports it per tagged instance, or summed with the
    /// tag off.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AConsumerHeldConsumerWideOnOneInstance_IsNotHeldOnAnother_AndIsCountedThere(bool tagInstances)
    {
        using var harness = new InstanceHarness(tagInstances: tagInstances);
        var clock = new FakeTimeProvider(Now);
        using var outbox = new OutboxMetrics(clock, harness.OutboxOptions, harness.InstanceOptions, harness.Registry);
        using var inbox = new InboxMetrics(harness.Inbox, clock, harness.InboxOptions, harness.InstanceOptions, harness.Registry);
        using var recorder = new MeterRecorder(inbox.Meter);
        var a = Track(harness, outbox, "a");
        var b = Track(harness, outbox, "b");

        a.Inbox.OnDown(Consumer, "t1", Now, out _);
        a.Inbox.Succeeded(OtherConsumer, "t9");
        a.Inbox.OnDown(Consumer, "t2", Now, out _).ShouldBe(InboxBackoff.OutageScope.Consumer);

        a.Inbox.IsBlocked(Consumer, "t3", Now, out _).ShouldBeTrue("control: held on a for a tenant that never failed");
        b.Inbox.IsBlocked(Consumer, "t3", Now, out _).ShouldBeFalse("b's state is its own");
        b.Inbox.ExclusionsAt(Now).ShouldBeSameAs(InboxClaimExclusions.None, "b's claims leave nothing out");
        Observe(recorder, InboxConsumers).ShouldBe(
            tagInstances
                ? [$"{InstanceHarness.Store}|a|=1", $"{InstanceHarness.Store}|b|=0"]
                : [$"{InstanceHarness.Store}|-|=1"]);
    }

    private static (StoreInstanceKey Key, OutboxEndpointBackoff Outbox, InboxBackoff Inbox) Track(
        InstanceHarness harness, OutboxMetrics outbox, string id)
    {
        var key = new StoreInstanceKey(InstanceHarness.Store, id);
        var backoff = new OutboxEndpointBackoff(harness.OutboxOptions.PollingInterval, harness.OutboxOptions.TransportUnavailableBackoff);
        outbox.Track(key, backoff);
        return (key, backoff, harness.Inbox.Add(key));
    }

    private static OutboxMetrics.SettledTally Tally()
    {
        var tally = default(OutboxMetrics.SettledTally);
        tally.Add(OutboxDispatchers.Transport, OutboxOutcomeKind.Published);
        return tally;
    }

    private static List<string> Observe(MeterRecorder recorder, string instrument)
        => [.. recorder.Observe(instrument)
            .Where(r => r.Tag("store.key") == InstanceHarness.Store.Value)
            .Select(r => $"{r.Tag("store.key")}|{r.Tag("store.instance") ?? "-"}|{r.Tag("endpoint")}={r.Value}")
            .Order(StringComparer.Ordinal)];
}
