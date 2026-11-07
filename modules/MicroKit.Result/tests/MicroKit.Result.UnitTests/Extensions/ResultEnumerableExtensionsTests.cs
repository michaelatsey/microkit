using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Extensions;

public sealed class ResultEnumerableExtensionsTests
{

    #region Partition

    [Fact]
    public void Partition_WhenMixed_SplitsValuesAndErrorsInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();
        Result<int>[] results =
        [
            Result.Success(1),
            Result.Failure<int>(first),
            Result.Success(2),
            Result.Failure<int>(second),
        ];

        // Act
        var (successes, failures) = results.Partition();

        // Assert
        successes.ShouldBe([1, 2]);
        failures.Count.ShouldBe(2);
        failures[0].ShouldBeSameAs(first);
        failures[1].ShouldBeSameAs(second);
    }

    [Fact]
    public void Partition_WhenAllSucceed_ReturnsNoFailures()
    {
        // Arrange
        Result<int>[] results = [Result.Success(1), Result.Success(2)];

        // Act
        var (successes, failures) = results.Partition();

        // Assert
        successes.ShouldBe([1, 2]);
        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Partition_WhenEmpty_ReturnsTwoEmptyLists()
    {
        // Arrange
        var results = Enumerable.Empty<Result<int>>();

        // Act
        var (successes, failures) = results.Partition();

        // Assert
        successes.ShouldBeEmpty();
        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Partition_WhenSequenceContainsDefault_ThrowsResultException()
    {
        // Arrange
        Result<int>[] results = [Result.Success(1), default];

        // Act + Assert
        Should.Throw<ResultException>(() => results.Partition());
    }

    [Fact]
    public void Partition_WhenSourceIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<Result<int>> results = null!;

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => results.Partition());
        thrown.ParamName.ShouldBe("results");
    }

    #endregion

    #region Successes

    [Fact]
    public void Successes_WhenMixed_YieldsSuccessValuesInOrder()
    {
        // Arrange
        Result<int>[] results =
        [
            Result.Success(1),
            Result.Failure<int>(new TestError()),
            Result.Success(2),
        ];

        // Act
        var values = results.Successes().ToList();

        // Assert
        values.ShouldBe([1, 2]);
    }

    [Fact]
    public void Successes_WhenAllFail_YieldsNothing()
    {
        // Arrange
        Result<int>[] results = [Result.Failure<int>(new TestError())];

        // Act
        var values = results.Successes().ToList();

        // Assert
        values.ShouldBeEmpty();
    }

    [Fact]
    public void Successes_WhenSourceIsNull_ThrowsArgumentNullExceptionOnEnumeration()
    {
        // Arrange
        IEnumerable<Result<int>> results = null!;

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => results.Successes().ToList());
        thrown.ParamName.ShouldBe("results");
    }

    [Fact]
    public void Successes_WhenSequenceContainsDefault_ThrowsResultException()
    {
        // Arrange
        Result<int>[] results = [Result.Success(1), default];

        // Act + Assert
        Should.Throw<ResultException>(() => results.Successes().ToList());
    }

    #endregion

    #region Failures

    [Fact]
    public void Failures_WhenMixed_YieldsSameErrorsInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();
        Result<int>[] results =
        [
            Result.Failure<int>(first),
            Result.Success(1),
            Result.Failure<int>(second),
        ];

        // Act
        var errors = results.Failures().ToList();

        // Assert
        errors.Count.ShouldBe(2);
        errors[0].ShouldBeSameAs(first);
        errors[1].ShouldBeSameAs(second);
    }

    [Fact]
    public void Failures_WhenAllSucceed_YieldsNothing()
    {
        // Arrange
        Result<int>[] results = [Result.Success(1), Result.Success(2)];

        // Act
        var errors = results.Failures().ToList();

        // Assert
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Failures_WhenSourceIsNull_ThrowsArgumentNullExceptionOnEnumeration()
    {
        // Arrange
        IEnumerable<Result<int>> results = null!;

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => results.Failures().ToList());
        thrown.ParamName.ShouldBe("results");
    }

    [Fact]
    public void Failures_WhenSequenceContainsDefault_ThrowsResultException()
    {
        // Arrange
        Result<int>[] results = [Result.Failure<int>(new TestError()), default];

        // Act + Assert
        Should.Throw<ResultException>(() => results.Failures().ToList());
    }

    #endregion

    #region Traverse

    [Fact]
    public void Traverse_WhenEverySelectionSucceeds_ReturnsValuesInOrder()
    {
        // Arrange
        int[] source = [1, 2, 3];

        // Act
        var traversed = source.Traverse(x => Result.Success(x * 10));

        // Assert
        traversed.IsSuccess.ShouldBeTrue();
        traversed.Value.ShouldBe([10, 20, 30]);
    }

    [Fact]
    public void Traverse_WhenSelectionFails_ReturnsFirstErrorAndStopsSelecting()
    {
        // Arrange
        var first = new NotFoundTestError(2);
        var selected = new List<int>();
        int[] source = [1, 2, 3, 4];

        // Act
        var traversed = source.Traverse(x =>
        {
            selected.Add(x);
            return x switch
            {
                2 => Result.Failure<int>(first),
                3 => Result.Failure<int>(new OtherError()),
                _ => Result.Success(x),
            };
        });

        // Assert
        traversed.IsFailure.ShouldBeTrue();
        traversed.Error.ShouldBeSameAs(first);
        selected.ShouldBe([1, 2]);
    }

    [Fact]
    public void Traverse_WhenSourceIsEmpty_ReturnsSuccessWithEmptyList()
    {
        // Arrange
        var called = false;
        var source = Enumerable.Empty<int>();

        // Act
        var traversed = source.Traverse(x =>
        {
            called = true;
            return Result.Success(x);
        });

        // Assert
        called.ShouldBeFalse();
        traversed.IsSuccess.ShouldBeTrue();
        traversed.Value.ShouldBeEmpty();
    }

    [Fact]
    public void Traverse_WhenSelectorReturnsDefault_ThrowsResultException()
    {
        // Arrange
        int[] source = [1];

        // Act + Assert
        Should.Throw<ResultException>(() => source.Traverse(_ => default(Result<int>)));
    }

    [Fact]
    public void Traverse_WhenSelectorThrows_PropagatesException()
    {
        // Arrange
        var boom = new InvalidOperationException("boom");
        int[] source = [1];

        // Act + Assert
        var thrown = Should.Throw<InvalidOperationException>(() => source.Traverse<int, int>(_ => throw boom));
        thrown.ShouldBeSameAs(boom);
    }

    [Fact]
    public void Traverse_WhenSourceIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<int> source = null!;

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => source.Traverse(x => Result.Success(x)));
        thrown.ParamName.ShouldBe("source");
    }

    [Fact]
    public void Traverse_WhenSelectorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        int[] source = [1];

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => source.Traverse<int, int>(null!));
        thrown.ParamName.ShouldBe("selector");
    }

    #endregion
}
