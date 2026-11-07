using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Core;

public sealed class ResultCombineTests
{

    #region Combine

    [Fact]
    public void Combine_WhenEmpty_ReturnsSuccess()
    {
        // Act
        var combined = Result.Combine();

        // Assert
        combined.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Combine_WhenAllSucceed_ReturnsSuccess()
    {
        // Act
        var combined = Result.Combine(Result.Success(), Result.Success(), Result.Success());

        // Assert
        combined.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Combine_WhenSeveralFail_ReturnsFirstFailureBySameError()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();

        // Act
        var combined = Result.Combine(
            Result.Success(),
            Result.Failure(first),
            Result.Failure(second));

        // Assert
        combined.IsFailure.ShouldBeTrue();
        combined.Error.ShouldBeSameAs(first);
    }

    [Fact]
    public void Combine_WhenAnyIsDefault_ThrowsResultException()
    {
        // Arrange
        Result uninitialized = default;

        // Act + Assert
        Should.Throw<ResultException>(() => Result.Combine(Result.Success(), uninitialized));
    }

    #endregion

    #region CombineAll

    [Fact]
    public void CombineAll_WhenEmpty_ReturnsSuccess()
    {
        // Act
        var combined = Result.CombineAll();

        // Assert
        combined.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CombineAll_WhenAllSucceed_ReturnsSuccess()
    {
        // Act
        var combined = Result.CombineAll(Result.Success(), Result.Success());

        // Assert
        combined.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CombineAll_WhenSeveralFail_ReturnsErrorCollectionWithEveryErrorInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();

        // Act
        var combined = Result.CombineAll(
            Result.Failure(first),
            Result.Success(),
            Result.Failure(second));

        // Assert
        combined.IsFailure.ShouldBeTrue();
        var collection = combined.Error.ShouldBeOfType<ErrorCollection>();
        collection.Count.ShouldBe(2);
        collection[0].ShouldBeSameAs(first);
        collection[1].ShouldBeSameAs(second);
    }

    [Fact]
    public void CombineAll_WhenOneFails_WrapsItsErrorInErrorCollection()
    {
        // Arrange
        var error = new TestError();

        // Act
        var combined = Result.CombineAll(Result.Success(), Result.Failure(error));

        // Assert
        combined.IsFailure.ShouldBeTrue();
        var collection = combined.Error.ShouldBeOfType<ErrorCollection>();
        collection.ShouldHaveSingleItem().ShouldBeSameAs(error);
    }

    [Fact]
    public void CombineAll_WhenAnyIsDefault_ThrowsResultException()
    {
        // Arrange
        Result uninitialized = default;

        // Act + Assert
        Should.Throw<ResultException>(() => Result.CombineAll(Result.Success(), uninitialized));
    }

    #endregion

    #region Combine (two values)

    [Fact]
    public void Combine_TwoValuesWhenBothSucceed_ReturnsTuple()
    {
        // Act
        var combined = Result.Combine(Result.Success(1), Result.Success("two"));

        // Assert
        combined.IsSuccess.ShouldBeTrue();
        combined.Value.ShouldBe((1, "two"));
    }

    [Fact]
    public void Combine_TwoValuesWhenBothFail_ReturnsFirstError()
    {
        // Arrange
        var first = new TestError("first");

        // Act
        var combined = Result.Combine(
            Result.Failure<int>(first),
            Result.Failure<string>(new OtherError()));

        // Assert
        combined.IsFailure.ShouldBeTrue();
        combined.Error.ShouldBeSameAs(first);
    }

    [Fact]
    public void Combine_TwoValuesWhenSecondFails_ReturnsSecondError()
    {
        // Arrange
        var error = new OtherError();

        // Act
        var combined = Result.Combine(Result.Success(1), Result.Failure<string>(error));

        // Assert
        combined.IsFailure.ShouldBeTrue();
        combined.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Combine_TwoValuesWhenFirstIsDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> uninitialized = default;

        // Act + Assert
        Should.Throw<ResultException>(() =>
            Result.Combine(uninitialized, Result.Success("two")));
    }

    [Fact]
    public void Combine_TwoValuesWhenSecondIsDefault_ThrowsResultException()
    {
        // Arrange
        Result<string> uninitialized = default;

        // Act + Assert
        Should.Throw<ResultException>(() =>
            Result.Combine(Result.Success(1), uninitialized));
    }

    #endregion

    #region Combine (three values)

    [Fact]
    public void Combine_ThreeValuesWhenAllSucceed_ReturnsTuple()
    {
        // Act
        var combined = Result.Combine(
            Result.Success(1),
            Result.Success("two"),
            Result.Success(3.0));

        // Assert
        combined.IsSuccess.ShouldBeTrue();
        combined.Value.ShouldBe((1, "two", 3.0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Combine_ThreeValuesWhenOneFails_ReturnsItsError(int failingIndex)
    {
        // Arrange
        var error = new NotFoundTestError(failingIndex);
        var first = failingIndex == 0 ? Result.Failure<int>(error) : Result.Success(1);
        var second = failingIndex == 1 ? Result.Failure<string>(error) : Result.Success("two");
        var third = failingIndex == 2 ? Result.Failure<double>(error) : Result.Success(3.0);

        // Act
        var combined = Result.Combine(first, second, third);

        // Assert
        combined.IsFailure.ShouldBeTrue();
        combined.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Combine_ThreeValuesWhenSeveralFail_ReturnsFirstError()
    {
        // Arrange
        var second = new TestError("second");

        // Act
        var combined = Result.Combine(
            Result.Success(1),
            Result.Failure<string>(second),
            Result.Failure<double>(new OtherError()));

        // Assert
        combined.IsFailure.ShouldBeTrue();
        combined.Error.ShouldBeSameAs(second);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Combine_ThreeValuesWhenOneIsDefault_ThrowsResultException(int defaultIndex)
    {
        // Arrange
        var first = defaultIndex == 0 ? default : Result.Success(1);
        var second = defaultIndex == 1 ? default : Result.Success("two");
        var third = defaultIndex == 2 ? default : Result.Success(3.0);

        // Act + Assert
        Should.Throw<ResultException>(() => Result.Combine(first, second, third));
    }

    #endregion

    #region CombineAll (generic)

    [Fact]
    public void CombineAll_GenericWhenEmpty_ReturnsSuccess()
    {
        // Act
        var combined = Result.CombineAll<int>();

        // Assert
        combined.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CombineAll_GenericWhenAllSucceed_ReturnsSuccess()
    {
        // Act
        var combined = Result.CombineAll(Result.Success(1), Result.Success(2));

        // Assert
        combined.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CombineAll_GenericWhenSeveralFail_ReturnsErrorCollectionWithEveryErrorInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();

        // Act
        var combined = Result.CombineAll(
            Result.Success(1),
            Result.Failure<int>(first),
            Result.Failure<int>(second));

        // Assert
        combined.IsFailure.ShouldBeTrue();
        var collection = combined.Error.ShouldBeOfType<ErrorCollection>();
        collection.Count.ShouldBe(2);
        collection[0].ShouldBeSameAs(first);
        collection[1].ShouldBeSameAs(second);
    }

    [Fact]
    public void CombineAll_GenericWhenAnyIsDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> uninitialized = default;

        // Act + Assert
        Should.Throw<ResultException>(() => Result.CombineAll(Result.Success(1), uninitialized));
    }

    #endregion
}
