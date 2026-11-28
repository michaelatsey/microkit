namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>What an inbox claim leaves out (#210 D5): built by the engine, read by any store provider.</summary>
public sealed class InboxClaimExclusionsTests
{
    [Fact]
    public void None_LeavesNothingOut_AndIsShared()
    {
        InboxClaimExclusions.None.IsEmpty.ShouldBeTrue();
        InboxClaimExclusions.None.ShouldBeSameAs(InboxClaimExclusions.None);
    }

    [Fact]
    public void IsEmpty_ReadsTheListsAsTheyAreNow()
    {
        var consumers = new List<string>();
        var excluded = new InboxClaimExclusions(consumers, []);
        excluded.IsEmpty.ShouldBeTrue("control");

        consumers.Add("Tests.ConsumerX, Tests");

        excluded.IsEmpty.ShouldBeFalse("a store reading IsEmpty first must not send the unfiltered claim");
    }

    [Fact]
    public void ANullOrEmptyConsumer_OrANullTenant_IsRefused()
    {
        Should.Throw<ArgumentException>(() => new InboxClaimExclusions([null!], [])).ParamName.ShouldBe("consumerTypes");
        Should.Throw<ArgumentException>(() => new InboxClaimExclusions([string.Empty], [])).ParamName.ShouldBe("consumerTypes");
        Should.Throw<ArgumentException>(() => new InboxClaimExclusions([], [("X", null!)])).ParamName.ShouldBe("pairs");
        Should.Throw<ArgumentException>(() => new InboxClaimExclusions([], [(string.Empty, "A")])).ParamName.ShouldBe("pairs");
        Should.Throw<ArgumentNullException>(() => new InboxClaimExclusions(null!, []));
        Should.Throw<ArgumentNullException>(() => new InboxClaimExclusions([], null!));
        new InboxClaimExclusions(["X"], [("Y", "A")]).IsEmpty.ShouldBeFalse("control: valid entries are accepted");
    }

    /// <summary>
    /// An empty tenant id is a tenant (PR #219 review): a delivery's <c>tenantid</c> header is copied as is, and
    /// a pair holding "" must not make every claim of the store throw.
    /// </summary>
    [Fact]
    public void APairWithAnEmptyTenantId_IsAccepted()
        => new InboxClaimExclusions([], [("X", string.Empty)]).Pairs.ShouldBe([("X", string.Empty)]);
}
