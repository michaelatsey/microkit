using MicroKit.Messaging.Stores;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// What a commit wakes (#215 D3): a single store's outbox loop; the instance a scope prepared for a
/// partitioned store serves; nothing else — and never an exception (V9).
/// </summary>
public sealed class OutboxCommitSignalTests
{
    private static readonly MessageStoreKey Single = new("tests:single");
    private static readonly MessageStoreKey Other = new("tests:other-single");
    private static readonly MessageStoreKey Partitioned = InstanceHarness.Store;

    [Fact]
    public void ACommitOfASingleStore_WakesItsLoop_AndNoOther()
    {
        using var harness = new InstanceHarness();
        var wakes = Wakes(harness);

        new OutboxCommitSignal(new DeliveryStoreHolder(), wakes).Committed(Single);

        Pending(wakes.Of(Single)!).ShouldBeTrue("a single store's commit wakes its loop, from any scope");
        Pending(wakes.Of(Other)!).ShouldBeFalse("another store's loop is not woken");
    }

    [Fact]
    public void ACommitInAnInstancesScope_WakesThatInstance_AndNotItsSibling()
    {
        using var harness = new InstanceHarness();
        var wakes = Wakes(harness);
        var i = harness.Start("i");
        var j = harness.Start("j");
        var holder = new DeliveryStoreHolder { Store = Partitioned, Instance = i };

        new OutboxCommitSignal(holder, wakes).Committed(Partitioned);

        Pending(i.OutboxWake).ShouldBeTrue("the instance the scope serves is woken");
        Pending(j.OutboxWake).ShouldBeFalse("its sibling is not");
    }

    [Fact]
    public void ACommitOfAPartitionedStore_FromAHostScope_WakesNothing()
    {
        using var harness = new InstanceHarness();
        var wakes = Wakes(harness);
        var i = harness.Start("i");

        Should.NotThrow(() => new OutboxCommitSignal(new DeliveryStoreHolder(), wakes).Committed(Partitioned));

        Pending(i.OutboxWake).ShouldBeFalse("a host scope names no instance: the row waits for the poll (#189)");
        wakes.Of(Partitioned).ShouldBeNull("a partitioned store has no single-store wake");
    }

    [Fact]
    public void ACommitOfAPartitionedStore_FromAnotherStoresDeliveryScope_WakesNothing()
    {
        using var harness = new InstanceHarness();
        var wakes = Wakes(harness);
        var i = harness.Start("i");
        var holder = new DeliveryStoreHolder { Store = Single };

        new OutboxCommitSignal(holder, wakes).Committed(Partitioned);

        Pending(i.OutboxWake).ShouldBeFalse("another store's delivery scope names no instance of this store");
        Pending(wakes.Of(Single)!).ShouldBeFalse("nor is the delivery's own store woken by another store's commit");
    }

    [Fact]
    public void ACommitOfASingleStore_FromAnInstancesScope_WakesTheSingleStore_AndNotTheInstance()
    {
        using var harness = new InstanceHarness();
        var wakes = Wakes(harness);
        var i = harness.Start("i");
        var holder = new DeliveryStoreHolder { Store = Partitioned, Instance = i };

        new OutboxCommitSignal(holder, wakes).Committed(Single);

        Pending(wakes.Of(Single)!).ShouldBeTrue("a single store's commit wakes it whatever the scope");
        Pending(i.OutboxWake).ShouldBeFalse("the scope's instance belongs to another store");
    }

    [Fact]
    public void ACommitOfAnUnknownStore_OrOfNoStore_ThrowsNothing_AndWakesNothing()
    {
        using var harness = new InstanceHarness();
        var wakes = Wakes(harness);
        var signal = new OutboxCommitSignal(new DeliveryStoreHolder(), wakes);

        Should.NotThrow(() => signal.Committed(new MessageStoreKey("tests:unknown")));
        Should.NotThrow(() => signal.Committed(null!));

        Pending(wakes.Of(Single)!).ShouldBeFalse();
        Pending(wakes.Of(Other)!).ShouldBeFalse();
    }

    [Fact]
    public void ACommitForARemovedAndReleasedInstance_ThrowsNothing()
    {
        using var harness = new InstanceHarness();
        var wakes = Wakes(harness);
        var i = harness.Start("i");
        var holder = new DeliveryStoreHolder { Store = Partitioned, Instance = i };
        i.Remove();
        harness.Registry.Remove(i);
        i.Dispose();

        Should.NotThrow(() => new OutboxCommitSignal(holder, wakes).Committed(Partitioned));
        harness.Registry.Find(i.Key).ShouldBeNull("the signal created no state for the removed instance");
    }

    [Fact]
    public void ManyCommits_LeaveOneSignalPending()
    {
        using var harness = new InstanceHarness();
        var wakes = Wakes(harness);
        var signal = new OutboxCommitSignal(new DeliveryStoreHolder(), wakes);

        Parallel.For(0, 1000, _ => signal.Committed(Single));

        var wake = wakes.Of(Single)!;
        wake.Reader.Count.ShouldBe(1, "signals coalesce: one pending claim, whatever the number of commits");
    }

    private static OutboxWakes Wakes(InstanceHarness harness)
    {
        var stores = Substitute.For<IMessageStoreSet>();
        stores.GetStores().Returns([Single, Other, Partitioned]);
        return new OutboxWakes(stores, harness.Registry);
    }

    private static bool Pending(LoopWake wake) => wake.Reader.TryPeek(out _);
}
