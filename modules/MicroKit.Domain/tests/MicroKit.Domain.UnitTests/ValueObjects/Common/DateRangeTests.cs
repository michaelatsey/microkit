using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

public class DateRangeTests
{
    private readonly DateTimeOffset _baseDate = new(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_ValidRange_ShouldCreateInstance()
    {
        // Arrange
        var start = _baseDate;
        var end = _baseDate.AddHours(2);

        // Act
        var dateRange = new DateRange(start, end);

        // Assert
        dateRange.Start.ShouldBe(start);
        dateRange.End.ShouldBe(end);
    }

    [Fact]
    public void Constructor_StartEqualsEnd_ShouldCreateInstance()
    {
        // Arrange
        var date = _baseDate;

        // Act
        var dateRange = new DateRange(date, date);

        // Assert
        dateRange.Start.ShouldBe(date);
        dateRange.End.ShouldBe(date);
    }

    [Fact]
    public void Constructor_StartAfterEnd_ShouldThrowDomainException()
    {
        // Arrange
        var start = _baseDate.AddHours(2);
        var end = _baseDate;

        // Act & Assert
        var ex = Should.Throw<DomainException>(() => new DateRange(start, end));
        ex.Message.ShouldBe("Start date cannot be after end date.");
    }

    [Fact]
    public void Duration_ShouldCalculateCorrectDuration()
    {
        // Arrange
        var start = _baseDate;
        var end = _baseDate.AddHours(3).AddMinutes(30);
        var dateRange = new DateRange(start, end);

        // Act
        var duration = dateRange.Duration;

        // Assert
        duration.ShouldBe(TimeSpan.FromHours(3.5));
    }

    [Theory]
    [InlineData(0, true)]      // At start
    [InlineData(1, true)]      // Within range
    [InlineData(2, true)]      // At end
    [InlineData(-1, false)]    // Before start
    [InlineData(3, false)]     // After end
    public void Contains_ShouldReturnCorrectResult(int hoursOffset, bool expected)
    {
        // Arrange
        var start = _baseDate;
        var end = _baseDate.AddHours(2);
        var dateRange = new DateRange(start, end);
        var testDate = _baseDate.AddHours(hoursOffset);

        // Act
        var result = dateRange.Contains(testDate);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void Overlaps_CompletelyOverlapping_ShouldReturnTrue()
    {
        // Arrange
        var range1 = new DateRange(_baseDate, _baseDate.AddHours(4));
        var range2 = new DateRange(_baseDate.AddHours(1), _baseDate.AddHours(3));

        // Act
        var result = range1.Overlaps(range2);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void Overlaps_PartiallyOverlapping_ShouldReturnTrue()
    {
        // Arrange
        var range1 = new DateRange(_baseDate, _baseDate.AddHours(3));
        var range2 = new DateRange(_baseDate.AddHours(2), _baseDate.AddHours(5));

        // Act
        var result = range1.Overlaps(range2);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void Overlaps_TouchingAtBoundary_ShouldReturnTrue()
    {
        // Arrange
        var range1 = new DateRange(_baseDate, _baseDate.AddHours(2));
        var range2 = new DateRange(_baseDate.AddHours(2), _baseDate.AddHours(4));

        // Act
        var result = range1.Overlaps(range2);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void Overlaps_NonOverlapping_ShouldReturnFalse()
    {
        // Arrange
        var range1 = new DateRange(_baseDate, _baseDate.AddHours(2));
        var range2 = new DateRange(_baseDate.AddHours(3), _baseDate.AddHours(5));

        // Act
        var result = range1.Overlaps(range2);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Overlaps_WithNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var range = new DateRange(_baseDate, _baseDate.AddHours(2));

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => range.Overlaps(null!));
    }

    [Fact]
    public void Create_ShouldCreateInstanceLikeConstructor()
    {
        // Arrange
        var start = _baseDate;
        var end = _baseDate.AddHours(2);

        // Act
        var dateRange = DateRange.Create(start, end);

        // Assert
        dateRange.Start.ShouldBe(start);
        dateRange.End.ShouldBe(end);
    }

    [Fact]
    public void ForDay_ShouldCreateDayRange()
    {
        // Arrange
        var date = new DateTimeOffset(2024, 3, 15, 14, 30, 45, TimeSpan.FromHours(2));

        // Act
        var dateRange = DateRange.ForDay(date);

        // Assert
        dateRange.Start.ShouldBe(new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.FromHours(2)));
        dateRange.End.Hour.ShouldBe(23);
        dateRange.End.Minute.ShouldBe(59);
        dateRange.End.Second.ShouldBe(59);
        dateRange.Duration.ShouldBeInRange(
            TimeSpan.FromDays(1).Subtract(TimeSpan.FromMilliseconds(1)),
            TimeSpan.FromDays(1).Add(TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void FromDuration_ValidDuration_ShouldCreateCorrectRange()
    {
        // Arrange
        var start = _baseDate;
        var duration = TimeSpan.FromHours(3);

        // Act
        var dateRange = DateRange.FromDuration(start, duration);

        // Assert
        dateRange.Start.ShouldBe(start);
        dateRange.End.ShouldBe(start.Add(duration));
        dateRange.Duration.ShouldBe(duration);
    }

    [Fact]
    public void FromDuration_NegativeDuration_ShouldThrowDomainException()
    {
        // Arrange
        var start = _baseDate;
        var duration = TimeSpan.FromHours(-1);

        // Act & Assert
        var ex = Should.Throw<DomainException>(() => DateRange.FromDuration(start, duration));
        ex.Message.ShouldBe("Duration cannot be negative.");
    }

    [Fact]
    public void ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var start = new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 3, 15, 15, 45, 0, TimeSpan.Zero);
        var dateRange = new DateRange(start, end);

        // Act
        var result = dateRange.ToString();

        // Assert
        result.ShouldBe("2024-03-15 10:30:00 +00:00 - 2024-03-15 15:45:00 +00:00");
    }

    [Fact]
    public void Equality_SameStartAndEnd_ShouldBeEqual()
    {
        // Arrange
        var start = _baseDate;
        var end = _baseDate.AddHours(2);
        var range1 = new DateRange(start, end);
        var range2 = new DateRange(start, end);

        // Act & Assert
        range1.ShouldBe(range2);
        range1.Equals(range2).ShouldBeTrue();
        (range1 == range2).ShouldBeTrue();
        (range1 != range2).ShouldBeFalse();
    }

    [Fact]
    public void Equality_DifferentRanges_ShouldNotBeEqual()
    {
        // Arrange
        var range1 = new DateRange(_baseDate, _baseDate.AddHours(2));
        var range2 = new DateRange(_baseDate.AddHours(1), _baseDate.AddHours(3));

        // Act & Assert
        range1.ShouldNotBe(range2);
        range1.Equals(range2).ShouldBeFalse();
        (range1 == range2).ShouldBeFalse();
        (range1 != range2).ShouldBeTrue();
    }

    [Fact]
    public void GetHashCode_EqualObjects_ShouldHaveSameHashCode()
    {
        // Arrange
        var start = _baseDate;
        var end = _baseDate.AddHours(2);
        var range1 = new DateRange(start, end);
        var range2 = new DateRange(start, end);

        // Act & Assert
        range1.GetHashCode().ShouldBe(range2.GetHashCode());
    }
}
