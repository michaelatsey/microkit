namespace MicroKit.Messaging.UnitTests.Publishing;

/// <summary>The wire-type rules of <see cref="MessageContractAttribute"/> (#186).</summary>
public sealed class MessageContractAttributeTests
{
    [Fact]
    public void WireType_IsTheNameAndVersion_AndTheVersionDefaultsToOne()
    {
        new MessageContractAttribute("shop.orders.order-placed").WireType
            .ShouldBe("shop.orders.order-placed.v1");

        new MessageContractAttribute("shop.orders.order-placed") { Version = 3 }.WireType
            .ShouldBe("shop.orders.order-placed.v3");
    }

    [Theory]
    [InlineData("shop.orders.order-placed.v1")]
    [InlineData("shop.orders.order-placed.v12")]
    public void Constructor_WhenTheNameAlreadyEndsInAVersionSuffix_Throws(string name)
    {
        // It would produce "...order-placed.v1.v1" on the wire, or — worse — a version that
        // disagrees with Version.
        var ex = Should.Throw<ArgumentException>(() => new MessageContractAttribute(name));

        ex.ParamName.ShouldBe("name");
        ex.Message.ShouldContain("Version");
    }

    [Theory]
    [InlineData("shop.orders.v")]
    [InlineData("shop.orders.vip")]
    [InlineData("shop.v2.orders")]
    public void Constructor_AcceptsANameThatOnlyLooksVersioned(string name)
        => new MessageContractAttribute(name).Name.ShouldBe(name);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Version_BelowOne_Throws(int version)
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new MessageContractAttribute("shop.orders.order-placed") { Version = version });

    [Fact]
    public void TheRegistry_KeysOnTheWireType()
    {
        var registry = IntegrationEventContractFixtures.Registry(
            ("/saasbtp/safety", e => e.Publishes<VersionedTwice>()));

        registry.ResolveContract(typeof(VersionedTwice)).ContractName
            .ShouldBe("saasbtp.safety.constat-amended.v2");
        registry.ResolveLocalType("saasbtp.safety.constat-amended.v2").ShouldBe(typeof(VersionedTwice));
    }
}

[MessageContract("saasbtp.safety.constat-amended", Version = 2)]
internal sealed record VersionedTwice(Guid ConstatId) : IIntegrationEvent;
