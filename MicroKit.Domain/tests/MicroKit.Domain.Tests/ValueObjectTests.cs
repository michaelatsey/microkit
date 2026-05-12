using MicroKit.Domain.Tests.Fakes;
using Xunit;

namespace MicroKit.Domain.Tests;

public sealed class ValueObjectTests
{
    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new Address("123 Main St", "Springfield");
        var b = new Address("123 Main St", "Springfield");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = new Address("123 Main St", "Springfield");
        var b = new Address("456 Elm St", "Springfield");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_DifferentTypes_ReturnsFalse()
    {
        var a = new Address("123 Main St", "Springfield");
        Assert.False(a.Equals("not an address"));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var a = new Address("123 Main St", "Springfield");
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void GetHashCode_EqualObjects_ReturnSameHash()
    {
        var a = new Address("123 Main St", "Springfield");
        var b = new Address("123 Main St", "Springfield");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentObjects_ReturnDifferentHash()
    {
        var a = new Address("123 Main St", "Springfield");
        var b = new Address("456 Elm St", "Shelbyville");
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

}
