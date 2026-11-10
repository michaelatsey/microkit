using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Core;

// ADR-RESULT-001 D3: one static factory per category on Error, returning a CategorizedError.
public sealed class CategorizedErrorTests
{
    private static readonly ErrorCode Code = ErrorCode.From("ORDER.ITEM.MISSING");

    public static TheoryData<ErrorCategory> AllCategories
    {
        get
        {
            var data = new TheoryData<ErrorCategory>();
            foreach (var category in Enum.GetValues<ErrorCategory>())
                data.Add(category);
            return data;
        }
    }

    // ── Constructor ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithCodeMessageAndCategory_ExposesThem()
    {
        // Arrange + Act
        var error = new CategorizedError(Code, "Item missing.", ErrorCategory.Conflict);

        // Assert
        error.Code.ShouldBe(Code);
        error.Message.ShouldBe("Item missing.");
        error.Category.ShouldBe(ErrorCategory.Conflict);
        error.Severity.ShouldBe(ErrorSeverity.Error);
        error.Metadata.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_WhenAssignedToIError_ExposesCategoryThroughInterface()
    {
        // Arrange
        var error = new CategorizedError(Code, "Item missing.", ErrorCategory.NotFound);

        // Act
        var category = CategoryThroughInterface(error);

        // Assert
        category.ShouldBe(ErrorCategory.NotFound);
    }

    // ── Factories ─────────────────────────────────────────────────────────

    [Fact]
    public void Factories_CoverEveryCategory_OnePerEnumValue()
    {
        // Arrange
        var categories = Enum.GetValues<ErrorCategory>();

        // Act
        var created = categories.Select(c => Create(c, Code, "m").Category).ToArray();

        // Assert
        categories.Length.ShouldBe(14);
        created.ShouldBe(categories);
    }

    [Theory]
    [MemberData(nameof(AllCategories))]
    public void Factory_WithCodeAndMessage_ReturnsCategorizedErrorOfThatCategory(ErrorCategory category)
    {
        // Act
        CategorizedError error = Create(category, Code, "Something failed.");

        // Assert
        error.ShouldBeOfType<CategorizedError>();
        error.Category.ShouldBe(category);
        error.Code.ShouldBe(Code);
        error.Message.ShouldBe("Something failed.");
        error.Severity.ShouldBe(ErrorSeverity.Error);
        error.Metadata.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllCategories))]
    public void Factory_WhenMessageIsNull_ThrowsArgumentNullException(ErrorCategory category)
    {
        // Act + Assert
        var thrown = Should.Throw<ArgumentNullException>(() => Create(category, Code, null!));
        thrown.ParamName.ShouldBe("message");
    }

    [Theory]
    [MemberData(nameof(AllCategories))]
    public void Factory_WhenMessageIsEmpty_ThrowsArgumentException(ErrorCategory category)
    {
        // Act + Assert
        var thrown = Should.Throw<ArgumentException>(() => Create(category, Code, string.Empty));
        thrown.ShouldBeOfType<ArgumentException>();
        thrown.ParamName.ShouldBe("message");
    }

    [Theory]
    [InlineData(ErrorCategory.NotFound, " ")]
    [InlineData(ErrorCategory.Conflict, "\t")]
    [InlineData(ErrorCategory.Technical, " \r\n ")]
    [InlineData(ErrorCategory.PreconditionFailed, "   ")]
    public void Factory_WhenMessageIsWhitespace_ThrowsArgumentException(ErrorCategory category, string message)
    {
        // Act + Assert
        var thrown = Should.Throw<ArgumentException>(() => Create(category, Code, message));
        thrown.ShouldBeOfType<ArgumentException>();
        thrown.ParamName.ShouldBe("message");
    }

    [Fact]
    public void Factory_WhenUsedAsFailure_KeepsCategoryOnResult()
    {
        // Arrange
        var error = Error.NotFound(Code, "Item missing.");

        // Act
        var result = Result.Failure<int>(error);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
        result.Error.Category.ShouldBe(ErrorCategory.NotFound);
    }

    // ── Equality ──────────────────────────────────────────────────────────

    [Fact]
    public void Equals_WhenSameCodeMessageAndCategory_ReturnsTrue()
    {
        // Arrange
        var first = Error.Conflict(Code, "Already exists.");
        var second = new CategorizedError(ErrorCode.From("ORDER.ITEM.MISSING"), "Already exists.", ErrorCategory.Conflict);

        // Act
        var equal = first.Equals(second);

        // Assert
        equal.ShouldBeTrue();
        (first == second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Equals_WhenCategoryDiffers_ReturnsFalse()
    {
        // Arrange
        var notFound = Error.NotFound(Code, "Same message.");
        var conflict = Error.Conflict(Code, "Same message.");

        // Act
        var equal = notFound.Equals(conflict);

        // Assert
        equal.ShouldBeFalse();
        (notFound != conflict).ShouldBeTrue();
    }

    [Fact]
    public void Equals_WhenCodeDiffers_ReturnsFalse()
    {
        // Arrange
        var first = Error.Validation(ErrorCode.From("A.B"), "Same message.");
        var second = Error.Validation(ErrorCode.From("A.C"), "Same message.");

        // Act
        var equal = first.Equals(second);

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void Equals_WhenMessageDiffers_ReturnsFalse()
    {
        // Arrange
        var first = Error.Timeout(Code, "First.");
        var second = Error.Timeout(Code, "Second.");

        // Act
        var equal = first.Equals(second);

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void Equals_WhenOtherErrorTypeWithSameCodeAndMessage_ReturnsFalse()
    {
        // Arrange
        Error categorized = Error.NotFound(ErrorCode.From("TEST.NOT_FOUND"), "Item 1 not found");
        Error domain = new NotFoundTestError(1);

        // Act
        var equal = categorized.Equals(domain);

        // Assert
        domain.Code.ShouldBe(categorized.Code);
        domain.Message.ShouldBe(categorized.Message);
        domain.Category.ShouldBe(categorized.Category);
        equal.ShouldBeFalse();
    }

    private static ErrorCategory CategoryThroughInterface<TError>(TError error)
        where TError : IError => error.Category;

    private static CategorizedError Create(ErrorCategory category, ErrorCode code, string message) => category switch
    {
        ErrorCategory.NotFound => Error.NotFound(code, message),
        ErrorCategory.Validation => Error.Validation(code, message),
        ErrorCategory.Unauthorized => Error.Unauthorized(code, message),
        ErrorCategory.Forbidden => Error.Forbidden(code, message),
        ErrorCategory.Conflict => Error.Conflict(code, message),
        ErrorCategory.TooManyRequests => Error.TooManyRequests(code, message),
        ErrorCategory.Technical => Error.Technical(code, message),
        ErrorCategory.Unavailable => Error.Unavailable(code, message),
        ErrorCategory.External => Error.External(code, message),
        ErrorCategory.BusinessRule => Error.BusinessRule(code, message),
        ErrorCategory.NotSupported => Error.NotSupported(code, message),
        ErrorCategory.Timeout => Error.Timeout(code, message),
        ErrorCategory.Cancelled => Error.Cancelled(code, message),
        ErrorCategory.PreconditionFailed => Error.PreconditionFailed(code, message),
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "No factory for this category."),
    };

    [Fact]
    public void Factory_WhenCodeIsUninitialized_ThrowsArgumentException()
    {
        // Act + Assert
        Should.Throw<ArgumentException>(() => Error.NotFound(default, "Missing.")).ParamName.ShouldBe("code");
    }

    [Fact]
    public void Map_WhenMapperReturnsNull_ThrowsWithAMessageThatPointsToBind()
    {
        // Arrange
        Result<int> result = Result.Success(1);

        // Act
        var thrown = Should.Throw<ArgumentNullException>(() => result.Map<int, string>(_ => null!));

        // Assert
        thrown.Message.ShouldContain("Bind");
    }

    [Fact]
    public void Constructor_WhenArgumentsAreInvalid_Throws()
    {
        // Act + Assert: the public constructor checks what the factories check
        Should.Throw<ArgumentException>(() => new CategorizedError(default, "Message.", ErrorCategory.NotFound));
        Should.Throw<ArgumentException>(() => new CategorizedError(ErrorCode.From("A.B"), " ", ErrorCategory.NotFound));
        Should.Throw<ArgumentOutOfRangeException>(() => new CategorizedError(ErrorCode.From("A.B"), "Message.", (ErrorCategory)99));
    }

    // #198 PR body: a code set through with is checked as the constructor checks it

    public static TheoryData<string> InvalidCodes() => new() { "default", "empty", "whitespace" };

    private static ErrorCode InvalidCode(string kind) => kind switch
    {
        "default" => default,
        "empty" => new ErrorCode(string.Empty),
        "whitespace" => new ErrorCode("   "),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(InvalidCodes))]
    public void With_WhenCodeIsInvalidThroughTheSubtype_ThrowsArgumentException(string kind)
    {
        // Arrange
        var error = Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), "Order 7 does not exist.");

        // Act + Assert
        Should.Throw<ArgumentException>(() => error with { Code = InvalidCode(kind) });
    }

    [Theory]
    [MemberData(nameof(InvalidCodes))]
    public void With_WhenCodeIsInvalidThroughError_ThrowsArgumentException(string kind)
    {
        // Arrange: the with expression binds to Error.Code
        Error error = Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), "Order 7 does not exist.");

        // Act + Assert
        Should.Throw<ArgumentException>(() => error with { Code = InvalidCode(kind) });
    }

    [Fact]
    public void With_WhenCodeIsValid_KeepsTheNewCode()
    {
        // Arrange
        var error = Error.NotFound(ErrorCode.From("ORDER.NOT_FOUND"), "Order 7 does not exist.");

        // Act
        var renamed = error with { Code = ErrorCode.From("ORDER.GONE") };

        // Assert
        renamed.Code.ShouldBe(ErrorCode.From("ORDER.GONE"));
        renamed.Category.ShouldBe(ErrorCategory.NotFound);
    }
}
