using MicroKit.Domain.Primitives;
using Xunit;

namespace MicroKit.Domain.Tests;

public sealed class ErrorTests
{
    [Fact]
    public void None_HasEmptyCodeAndNoneType()
    {
        Assert.Equal(string.Empty, Error.None.Code);
        Assert.Equal(ErrorType.None, Error.None.Type);
    }

    [Fact]
    public void Failure_SetsCorrectType()
    {
        var e = Error.Failure("Order.Failed", "Order processing failed");
        Assert.Equal(ErrorType.Failure, e.Type);
        Assert.Equal("Order.Failed", e.Code);
        Assert.Equal("Order processing failed", e.Message);
    }

    [Fact]
    public void NotFound_SetsCorrectType()
    {
        var e = Error.NotFound("Order.NotFound", "Order not found");
        Assert.Equal(ErrorType.NotFound, e.Type);
    }

    [Fact]
    public void Conflict_SetsCorrectType()
    {
        var e = Error.Conflict("Order.Conflict", "Duplicate order");
        Assert.Equal(ErrorType.Conflict, e.Type);
    }

    [Fact]
    public void Validation_SetsCorrectType()
    {
        var e = Error.Validation("Name.Required", "Name is required");
        Assert.Equal(ErrorType.Validation, e.Type);
    }

    [Fact]
    public void Unauthorized_SetsCorrectType()
    {
        var e = Error.Unauthorized("Auth.Unauthorized", "Not authenticated");
        Assert.Equal(ErrorType.Unauthorized, e.Type);
    }

    [Fact]
    public void Forbidden_SetsCorrectType()
    {
        var e = Error.Forbidden("Auth.Forbidden", "Access denied");
        Assert.Equal(ErrorType.Forbidden, e.Type);
    }

    [Fact]
    public void Equals_SameCodeAndType_ReturnsTrue()
    {
        var a = Error.Failure("X", "msg");
        var b = Error.Failure("X", "msg");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentCode_ReturnsFalse()
    {
        var a = Error.Failure("X", "msg");
        var b = Error.Failure("Y", "msg");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_DifferentType_ReturnsFalse()
    {
        var a = Error.Failure("X", "msg");
        var b = Error.NotFound("X", "msg");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EqualityOperator_Works()
    {
        var a = Error.Failure("X", "msg");
        var b = Error.Failure("X", "msg");
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void ToString_None_ReturnsNone()
    {
        Assert.Equal("None", Error.None.ToString());
    }

    [Fact]
    public void ToString_ErrorWithCode_ReturnsCodeColonMessage()
    {
        var e = Error.Failure("Order.Failed", "Something went wrong");
        Assert.Equal("Order.Failed: Something went wrong", e.ToString());
    }

    [Fact]
    public void GetHashCode_EqualErrors_ReturnSameHash()
    {
        var a = Error.Failure("X", "msg");
        var b = Error.Failure("X", "msg");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
