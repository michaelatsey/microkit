namespace MicroKit.Result.UnitTests.Core;

public sealed class UnitTests
{
    [Fact]
    public void Value_WhenRead_EqualsDefault()
    {
        // Act
        var value = Unit.Value;

        // Assert
        value.ShouldBe(default(Unit));
        value.ShouldBe(new Unit());
    }

    [Fact]
    public void Equals_WhenTwoUnits_ReturnsTrue()
    {
        // Arrange
        var left = Unit.Value;
        var right = new Unit();

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeTrue();
        (left == right).ShouldBeTrue();
        (left != right).ShouldBeFalse();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void EqualsObject_WhenBoxedUnit_ReturnsTrue()
    {
        // Arrange
        object boxed = new Unit();

        // Act
        var equal = Unit.Value.Equals(boxed);

        // Assert
        equal.ShouldBeTrue();
    }

    [Fact]
    public void EqualsObject_WhenNullOrOtherType_ReturnsFalse()
    {
        // Act
        var equalsNull = Unit.Value.Equals(null);
        var equalsOther = Unit.Value.Equals((object)0);

        // Assert
        equalsNull.ShouldBeFalse();
        equalsOther.ShouldBeFalse();
    }

    [Fact]
    public void ToString_WhenCalled_ReturnsEmptyParentheses()
    {
        // Act
        var text = Unit.Value.ToString();

        // Assert
        text.ShouldBe("()");
    }

    [Fact]
    public void Success_WhenValueIsUnit_IsSuccessWithUnit()
    {
        // Act
        Result<Unit> result = Result.Success(Unit.Value);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
    }
}
