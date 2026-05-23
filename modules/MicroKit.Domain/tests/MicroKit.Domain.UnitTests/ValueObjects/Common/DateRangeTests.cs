using FluentAssertions;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
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
        dateRange.Start.Should().Be(start);
        dateRange.End.Should().Be(end);
    }

    [Fact]
    public void Constructor_StartEqualsEnd_ShouldCreateInstance()
    {
        // Arrange
        var date = _baseDate;

        // Act
        var dateRange = new DateRange(date, date);

        // Assert
        dateRange.Start.Should().Be(date);
        dateRange.End.Should().Be(date);
    }

    [Fact]
    public void Constructor_StartAfterEnd_ShouldThrowDomainException()
    {
        // Arrange
        var start = _baseDate.AddHours(2);
        var end = _baseDate;

        // Act & Assert
        var act = () => new DateRange(start, end);
        act.Should().Throw<DomainException>()
           .WithMessage("Start date cannot be after end date.");
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
        duration.Should().Be(TimeSpan.FromHours(3.5));
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
        result.Should().Be(expected);
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
        result.Should().BeTrue();
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
        result.Should().BeTrue();
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
        result.Should().BeTrue();
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
        result.Should().BeFalse();
    }

    [Fact]
    public void Overlaps_WithNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var range = new DateRange(_baseDate, _baseDate.AddHours(2));

        // Act & Assert
        var act = () => range.Overlaps(null!);
        act.Should().Throw<ArgumentNullException>();
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
        dateRange.Start.Should().Be(start);
        dateRange.End.Should().Be(end);
    }

    [Fact]
    public void ForDay_ShouldCreateDayRange()
    {
        // Arrange
        var date = new DateTimeOffset(2024, 3, 15, 14, 30, 45, TimeSpan.FromHours(2));

        // Act
        var dateRange = DateRange.ForDay(date);

        // Assert
        dateRange.Start.Should().Be(new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.FromHours(2)));
        dateRange.End.Hour.Should().Be(23);
        dateRange.End.Minute.Should().Be(59);
        dateRange.End.Second.Should().Be(59);
        dateRange.Duration.Should().BeCloseTo(TimeSpan.FromDays(1), TimeSpan.FromMilliseconds(1));
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
        dateRange.Start.Should().Be(start);
        dateRange.End.Should().Be(start.Add(duration));
        dateRange.Duration.Should().Be(duration);
    }

    [Fact]
    public void FromDuration_NegativeDuration_ShouldThrowDomainException()
    {
        // Arrange
        var start = _baseDate;
        var duration = TimeSpan.FromHours(-1);

        // Act & Assert
        var act = () => DateRange.FromDuration(start, duration);
        act.Should().Throw<DomainException>()
           .WithMessage("Duration cannot be negative.");
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
        result.Should().Be("2024-03-15 10:30:00 +00:00 - 2024-03-15 15:45:00 +00:00");
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
        range1.Should().Be(range2);
        range1.Equals(range2).Should().BeTrue();
        (range1 == range2).Should().BeTrue();
        (range1 != range2).Should().BeFalse();
    }

    [Fact]
    public void Equality_DifferentRanges_ShouldNotBeEqual()
    {
        // Arrange
        var range1 = new DateRange(_baseDate, _baseDate.AddHours(2));
        var range2 = new DateRange(_baseDate.AddHours(1), _baseDate.AddHours(3));

        // Act & Assert
        range1.Should().NotBe(range2);
        range1.Equals(range2).Should().BeFalse();
        (range1 == range2).Should().BeFalse();
        (range1 != range2).Should().BeTrue();
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
        range1.GetHashCode().Should().Be(range2.GetHashCode());
    }
}