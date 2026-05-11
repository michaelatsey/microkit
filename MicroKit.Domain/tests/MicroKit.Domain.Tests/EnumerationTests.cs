using MicroKit.Domain.Abstractions;
using MicroKit.Domain.Tests.Fakes;
using Xunit;

namespace MicroKit.Domain.Tests;

public sealed class EnumerationTests
{
    [Fact]
    public void GetAll_ReturnsAllStaticInstances()
    {
        var all = Enumeration.GetAll<OrderStatus>().ToList();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void FromId_ReturnsCorrectInstance()
    {
        var status = Enumeration.FromId<OrderStatus>(2);
        Assert.Same(OrderStatus.Completed, status);
    }

    [Fact]
    public void FromId_InvalidId_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Enumeration.FromId<OrderStatus>(99));
    }

    [Fact]
    public void FromName_ReturnsCorrectInstance()
    {
        var status = Enumeration.FromName<OrderStatus>("Pending");
        Assert.Same(OrderStatus.Pending, status);
    }

    [Fact]
    public void FromName_CaseInsensitive_Succeeds()
    {
        var status = Enumeration.FromName<OrderStatus>("PENDING");
        Assert.Same(OrderStatus.Pending, status);
    }

    [Fact]
    public void FromName_InvalidName_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Enumeration.FromName<OrderStatus>("Unknown"));
    }

    [Fact]
    public void Equals_SameId_ReturnsTrue()
    {
        Assert.Equal(OrderStatus.Pending, OrderStatus.Pending);
    }

    [Fact]
    public void Equals_DifferentId_ReturnsFalse()
    {
        Assert.NotEqual(OrderStatus.Pending, OrderStatus.Completed);
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        Assert.Equal("Cancelled", OrderStatus.Cancelled.ToString());
    }

    [Fact]
    public void AbsoluteDifference_ReturnsCorrectValue()
    {
        var diff = Enumeration.AbsoluteDifference(OrderStatus.Pending, OrderStatus.Completed);
        Assert.Equal(1, diff);
    }

    [Fact]
    public void CompareTo_OrdersById()
    {
        Assert.True(OrderStatus.Pending.CompareTo(OrderStatus.Completed) < 0);
        Assert.True(OrderStatus.Completed.CompareTo(OrderStatus.Pending) > 0);
        Assert.Equal(0, OrderStatus.Cancelled.CompareTo(OrderStatus.Cancelled));
    }

    [Fact]
    public void GetHashCode_SameId_SameHash()
    {
        Assert.Equal(OrderStatus.Pending.GetHashCode(), OrderStatus.Pending.GetHashCode());
    }
}
