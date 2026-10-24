using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

public sealed class AddressTests
{
    [Fact]
    public void Constructor_PaddedComponents_TrimsEach()
    {
        var address = new Address("  10 rue de la Paix ", " Paris ", " 75002 ", " FR ");

        address.Street.ShouldBe("10 rue de la Paix");
        address.City.ShouldBe("Paris");
        address.PostalCode.ShouldBe("75002");
        address.Country.ShouldBe("FR");
    }

    [Theory]
    [InlineData(null, "Paris", "75002", "FR", "Street")]
    [InlineData("10 rue de la Paix", " ", "75002", "FR", "City")]
    [InlineData("10 rue de la Paix", "Paris", "", "FR", "PostalCode")]
    [InlineData("10 rue de la Paix", "Paris", "75002", "\t", "Country")]
    public void Constructor_EmptyComponent_ThrowsDomainExceptionNamingIt(
        string? street, string? city, string? postalCode, string? country, string component)
    {
        Should.Throw<DomainException>(() => new Address(street!, city!, postalCode!, country!))
            .Message.ShouldBe($"Address {component} cannot be null or empty.");
    }

    [Fact]
    public void Equality_IsStructural_AfterTrimming()
    {
        Address.Create("10 rue de la Paix", "Paris", "75002", "FR")
            .ShouldBe(new Address(" 10 rue de la Paix", "Paris ", "75002", "FR"));
    }

    [Fact]
    public void ToString_ValidAddress_JoinsTheComponents()
    {
        new Address("10 rue de la Paix", "Paris", "75002", "FR").ToString()
            .ShouldBe("10 rue de la Paix, Paris, 75002, FR");
    }
}
