using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Extensions;

public sealed class ResultLinqExtensionsTests
{
    #region Select

    [Fact]
    public void Select_WhenSuccess_ProjectsValue()
    {
        // Arrange
        var result = Result.Success(21);

        // Act
        var projected = result.Select(x => x * 2);

        // Assert
        projected.IsSuccess.ShouldBeTrue();
        projected.Value.ShouldBe(42);
    }

    [Fact]
    public void Select_WithQuerySyntax_ProjectsValue()
    {
        // Arrange
        var result = Result.Success(21);

        // Act
        var projected = from x in result select x * 2;

        // Assert
        projected.IsSuccess.ShouldBeTrue();
        projected.Value.ShouldBe(42);
    }

    [Fact]
    public void Select_WhenFailure_PropagatesSameErrorWithoutInvokingSelector()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var projected = result.Select(x =>
        {
            called = true;
            return x;
        });

        // Assert
        called.ShouldBeFalse();
        projected.IsFailure.ShouldBeTrue();
        projected.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Select_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Select(x => x));
    }

    [Fact]
    public void Select_WhenSelectorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        Should.Throw<ArgumentNullException>(() => result.Select<int, int>(null!));
    }

    #endregion

    #region SelectMany (bind)

    [Fact]
    public void SelectMany_WhenSuccess_ReturnsSelectorResult()
    {
        // Arrange
        var result = Result.Success(21);

        // Act
        var bound = result.SelectMany(x => Result.Success(x * 2));

        // Assert
        bound.IsSuccess.ShouldBeTrue();
        bound.Value.ShouldBe(42);
    }

    [Fact]
    public void SelectMany_WhenSelectorFails_ReturnsSelectorError()
    {
        // Arrange
        var error = new OtherError();
        var result = Result.Success(21);

        // Act
        var bound = result.SelectMany(_ => Result.Failure<int>(error));

        // Assert
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void SelectMany_WhenFailure_PropagatesSameErrorWithoutInvokingSelector()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var bound = result.SelectMany(x =>
        {
            called = true;
            return Result.Success(x);
        });

        // Assert
        called.ShouldBeFalse();
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void SelectMany_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.SelectMany(x => Result.Success(x)));
    }

    [Fact]
    public void SelectMany_WhenSelectorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        Should.Throw<ArgumentNullException>(() => result.SelectMany<int, int>(null!));
    }

    #endregion

    #region SelectMany (bind and project)

    [Fact]
    public void SelectMany_WithProjectionWhenBothSucceed_CombinesValues()
    {
        // Arrange
        var result = Result.Success(2);

        // Act
        var combined = result.SelectMany(x => Result.Success(x * 10), (x, y) => x + y);

        // Assert
        combined.IsSuccess.ShouldBeTrue();
        combined.Value.ShouldBe(22);
    }

    [Fact]
    public void SelectMany_WithQuerySyntaxWhenEverySourceSucceeds_CombinesValues()
    {
        // Arrange
        var first = Result.Success(1);
        var second = Result.Success(20);
        var third = Result.Success(300);

        // Act
        var combined =
            from a in first
            from b in second
            from c in third
            select a + b + c;

        // Assert
        combined.IsSuccess.ShouldBeTrue();
        combined.Value.ShouldBe(321);
    }

    [Fact]
    public void SelectMany_WithQuerySyntaxWhenMiddleSourceFails_ReturnsItsErrorWithoutEvaluatingLaterSteps()
    {
        // Arrange
        var laterCalls = 0;
        var error = new NotFoundTestError(2);

        // Act
        var combined =
            from a in Result.Success(1)
            from b in Result.Failure<int>(error)
            from c in Later(a + b)
            select a + b + c;

        // Assert
        laterCalls.ShouldBe(0);
        combined.IsFailure.ShouldBeTrue();
        combined.Error.ShouldBeSameAs(error);

        Result<int> Later(int value)
        {
            laterCalls++;
            return Result.Success(value);
        }
    }

    [Fact]
    public void SelectMany_WithProjectionWhenSourceFails_PropagatesSameErrorWithoutInvokingSelectors()
    {
        // Arrange
        var called = false;
        var error = new TestError();
        var result = Result.Failure<int>(error);

        // Act
        var combined = result.SelectMany(
            x =>
            {
                called = true;
                return Result.Success(x);
            },
            (x, y) =>
            {
                called = true;
                return x + y;
            });

        // Assert
        called.ShouldBeFalse();
        combined.IsFailure.ShouldBeTrue();
        combined.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void SelectMany_WithProjectionWhenIntermediateFails_ReturnsIntermediateErrorWithoutProjecting()
    {
        // Arrange
        var projected = false;
        var error = new OtherError();
        var result = Result.Success(1);

        // Act
        var combined = result.SelectMany(
            _ => Result.Failure<int>(error),
            (x, y) =>
            {
                projected = true;
                return x + y;
            });

        // Assert
        projected.ShouldBeFalse();
        combined.IsFailure.ShouldBeTrue();
        combined.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void SelectMany_WithProjectionWhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.SelectMany(x => Result.Success(x), (x, y) => x + y));
    }

    [Fact]
    public void SelectMany_WithProjectionWhenIntermediateIsDefault_ThrowsResultException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        Should.Throw<ResultException>(() => result.SelectMany(_ => default(Result<int>), (x, y) => x + y));
    }

    [Fact]
    public void SelectMany_WithProjectionWhenCollectionSelectorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() =>
            result.SelectMany<int, int, int>(null!, (x, y) => x + y));
        thrown.ParamName.ShouldBe("collectionSelector");
    }

    [Fact]
    public void SelectMany_WithProjectionWhenResultSelectorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() =>
            result.SelectMany<int, int, int>(x => Result.Success(x), null!));
        thrown.ParamName.ShouldBe("resultSelector");
    }

    #endregion

    #region Where

    [Fact]
    public void Where_WhenPredicateTrue_ReturnsSameResult()
    {
        // Arrange
        var result = Result.Success(10);

        // Act
        var filtered = result.Where(x => x > 5, new TestError());

        // Assert
        filtered.ShouldBe(result);
    }

    [Fact]
    public void Where_WhenPredicateFalse_ReturnsFailureWithSameError()
    {
        // Arrange
        var error = new TestError("too small");
        var result = Result.Success(1);

        // Act
        var filtered = result.Where(x => x > 5, error);

        // Assert
        filtered.IsFailure.ShouldBeTrue();
        filtered.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public void Where_WhenFailure_PropagatesOriginalErrorWithoutInvokingPredicate()
    {
        // Arrange
        var called = false;
        var original = new TestError();
        var result = Result.Failure<int>(original);

        // Act
        var filtered = result.Where(
            _ =>
            {
                called = true;
                return false;
            },
            new OtherError());

        // Assert
        called.ShouldBeFalse();
        filtered.IsFailure.ShouldBeTrue();
        filtered.Error.ShouldBeSameAs(original);
    }

    [Fact]
    public void Where_WhenDefault_ThrowsResultException()
    {
        // Arrange
        Result<int> result = default;

        // Act + Assert
        Should.Throw<ResultException>(() => result.Where(_ => true, new TestError()));
    }

    [Fact]
    public void Where_WhenPredicateIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => result.Where(null!, new TestError()));
        thrown.ParamName.ShouldBe("predicate");
    }

    [Fact]
    public void Where_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var result = Result.Success(1);

        // Act + Assert
        Should.Throw<ArgumentNullException>(() => result.Where(_ => true, null!));
    }

    #endregion

    #region AsEnumerable

    [Fact]
    public void AsEnumerable_WhenSuccess_YieldsValueOnce()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var values = result.AsEnumerable().ToList();

        // Assert
        values.ShouldHaveSingleItem().ShouldBe(42);
    }

    [Fact]
    public void AsEnumerable_WhenFailure_YieldsNothing()
    {
        // Arrange
        var result = Result.Failure<int>(new TestError());

        // Act
        var values = result.AsEnumerable().ToList();

        // Assert
        values.ShouldBeEmpty();
    }

    [Fact]
    public void AsEnumerable_WhenDefault_YieldsNothing()
    {
        // Arrange
        Result<int> result = default;

        // Act
        var values = result.AsEnumerable().ToList();

        // Assert
        values.ShouldBeEmpty();
    }

    #endregion
}
