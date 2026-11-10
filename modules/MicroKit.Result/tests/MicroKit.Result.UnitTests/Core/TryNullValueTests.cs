using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Core;

// ADR-RESULT-001 D6: a null success value throws ArgumentNullException. A Try/TryAsync delegate that returns null
// violates its contract: the exception reaches the caller, outside the catch, and the mapper never sees it.
public sealed class TryNullValueTests
{
    // ── Success ───────────────────────────────────────────────────────────

    [Fact]
    public void Success_WhenNullableValueTypeIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => Result.Success<int?>(null));
        thrown.ParamName.ShouldBe("value");
    }

    [Fact]
    public void Success_WhenReferenceIsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => Result<string>.Success(null!));
        thrown.ParamName.ShouldBe("value");
    }

    [Fact]
    public void Success_WhenNullableValueTypeHasValue_ReturnsSuccess()
    {
        // Act
        var result = Result.Success<int?>(5);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(5);
    }

    [Fact]
    public void ImplicitFromValue_WhenNull_ThrowsArgumentNullException()
    {
        // Arrange
        string? value = null;

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() =>
        {
            Result<string> result = value!;
            return result;
        });
        thrown.ParamName.ShouldBe("value");
    }

    // ── Try ───────────────────────────────────────────────────────────────

    [Fact]
    public void Try_WhenFuncReturnsNull_ThrowsArgumentNullException()
    {
        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => Result<string>.Try(() => null!));
        thrown.ParamName.ShouldBe("value");
    }

    [Fact]
    public void Try_WhenFuncReturnsNullNullableValueType_ThrowsArgumentNullException()
    {
        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => Result<int?>.Try(() => null));
        thrown.ParamName.ShouldBe("value");
    }

    [Fact]
    public void Try_WithMapper_WhenFuncReturnsNull_ThrowsArgumentNullExceptionWithoutCallingMapper()
    {
        // Arrange
        var mapperCalls = new List<Exception>();

        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => Result<string>.Try(() => null!, ex =>
        {
            mapperCalls.Add(ex);
            return new TestError();
        }));
        thrown.ParamName.ShouldBe("value");
        mapperCalls.ShouldBeEmpty();
    }

    [Fact]
    public void Try_WhenFuncReturnsResult_ThrowsArgumentException()
    {
        // Arrange
        var error = new TestError();

        // Act + Assert
        var thrown = Should.Throw<ArgumentException>(() => Result<object>.Try(() => Result.Failure<int>(error)));
        thrown.ShouldBeOfType<ArgumentException>();
        thrown.ParamName.ShouldBe("value");
    }

    [Fact]
    public void Try_WithMapper_WhenFuncReturnsResult_ThrowsArgumentExceptionWithoutCallingMapper()
    {
        // Arrange
        var mapperCalls = new List<Exception>();

        // Act + Assert
        var thrown = Should.Throw<ArgumentException>(() => Result<object>.Try(() => Result.Success(), ex =>
        {
            mapperCalls.Add(ex);
            return new TestError();
        }));
        thrown.ShouldBeOfType<ArgumentException>();
        mapperCalls.ShouldBeEmpty();
    }

    // ── TryAsync ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryAsync_WhenFuncReturnsNull_ThrowsArgumentNullException(bool yield)
    {
        // Act + Assert
        var thrown = await Should.ThrowAsync<ArgumentNullException>(async () =>
            await Result<string>.TryAsync(() => NullAfter(yield)));
        thrown.ParamName.ShouldBe("value");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryAsync_WithMapper_WhenFuncReturnsNull_ThrowsArgumentNullExceptionWithoutCallingMapper(bool yield)
    {
        // Arrange
        var mapperCalls = new List<Exception>();

        // Act + Assert
        var thrown = await Should.ThrowAsync<ArgumentNullException>(async () =>
            await Result<string>.TryAsync(() => NullAfter(yield), ex =>
            {
                mapperCalls.Add(ex);
                return new TestError();
            }));
        thrown.ParamName.ShouldBe("value");
        mapperCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task TryAsync_WhenFuncReturnsResult_ThrowsArgumentException()
    {
        // Arrange
        var error = new TestError();

        // Act + Assert
        var thrown = await Should.ThrowAsync<ArgumentException>(async () =>
            await Result<object>.TryAsync(async () =>
            {
                await Task.Yield();
                return Result.Failure<int>(error);
            }));
        thrown.ShouldBeOfType<ArgumentException>();
    }

    [Fact]
    public async Task TryAsync_WithMapper_WhenFuncReturnsResult_ThrowsArgumentExceptionWithoutCallingMapper()
    {
        // Arrange
        var mapperCalls = new List<Exception>();

        // Act + Assert
        var thrown = await Should.ThrowAsync<ArgumentException>(async () =>
            await Result<object>.TryAsync(
                async () =>
                {
                    await Task.Yield();
                    return Result.Failure(new TestError());
                },
                ex =>
                {
                    mapperCalls.Add(ex);
                    return new TestError();
                }));
        thrown.ShouldBeOfType<ArgumentException>();
        mapperCalls.ShouldBeEmpty();
    }

    private static async Task<string> NullAfter(bool yield)
    {
        if (yield)
            await Task.Yield();
        return null!;
    }
}
