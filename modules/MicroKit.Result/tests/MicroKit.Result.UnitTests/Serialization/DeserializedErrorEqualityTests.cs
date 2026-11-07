using System.Collections.Immutable;
using System.Text.Json;
using MicroKit.Result.Serialization;

namespace MicroKit.Result.UnitTests.Serialization;

// #195: an error read back from JSON compares its Metadata by content, not by dictionary reference: two reads of the
// same payload are equal, and a JsonElement value compares by its JSON content.
public sealed class DeserializedErrorEqualityTests
{
    // ── DeserializedError ─────────────────────────────────────────────────

    [Fact]
    public void Equals_WhenDistinctDictionariesHoldSameEntries_ReturnsTrue()
    {
        // Arrange
        var left = ErrorWith(new Dictionary<string, object?> { ["retryAfter"] = 30L, ["region"] = "eu" });
        var right = ErrorWith(new Dictionary<string, object?> { ["retryAfter"] = 30L, ["region"] = "eu" });

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeTrue();
        left.ShouldBe(right);
        left.Equals(right).ShouldBeTrue();
    }

    [Fact]
    public void GetHashCode_WhenDistinctDictionariesHoldSameEntries_IsEqual()
    {
        // Arrange
        var left = ErrorWith(new Dictionary<string, object?> { ["retryAfter"] = 30L, ["region"] = "eu" });
        var right = ErrorWith(new Dictionary<string, object?> { ["retryAfter"] = 30L, ["region"] = "eu" });

        // Act
        var leftHash = left.GetHashCode();
        var rightHash = right.GetHashCode();

        // Assert
        leftHash.ShouldBe(rightHash);
    }

    [Fact]
    public void Equals_WhenSameEntriesInsertedInDifferentOrder_ReturnsTrue()
    {
        // Arrange
        var left = ErrorWith(new Dictionary<string, object?> { ["a"] = 1L, ["b"] = "two" });
        var right = ErrorWith(new Dictionary<string, object?> { ["b"] = "two", ["a"] = 1L });

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Equals_WhenSameKeyHasDifferentValue_ReturnsFalse()
    {
        // Arrange
        var left = ErrorWith(new Dictionary<string, object?> { ["retryAfter"] = 30L });
        var right = ErrorWith(new Dictionary<string, object?> { ["retryAfter"] = 60L });

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeFalse();
        left.ShouldNotBe(right);
    }

    [Fact]
    public void Equals_WhenOneSideHasMissingKey_ReturnsFalse()
    {
        // Arrange
        var left = ErrorWith(new Dictionary<string, object?> { ["a"] = 1L, ["b"] = 2L });
        var right = ErrorWith(new Dictionary<string, object?> { ["a"] = 1L });

        // Act
        var leftToRight = left.Equals(right);
        var rightToLeft = right.Equals(left);

        // Assert
        leftToRight.ShouldBeFalse();
        rightToLeft.ShouldBeFalse();
    }

    [Fact]
    public void Equals_WhenSameCountButDifferentKeys_ReturnsFalse()
    {
        // Arrange
        var left = ErrorWith(new Dictionary<string, object?> { ["a"] = 1L });
        var right = ErrorWith(new Dictionary<string, object?> { ["b"] = 1L });

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void Equals_WhenBothValuesAreNull_ReturnsTrue()
    {
        // Arrange
        var left = ErrorWith(new Dictionary<string, object?> { ["correlationId"] = null });
        var right = ErrorWith(new Dictionary<string, object?> { ["correlationId"] = null });

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Equals_WhenOneValueIsNull_ReturnsFalse()
    {
        // Arrange
        var left = ErrorWith(new Dictionary<string, object?> { ["correlationId"] = null });
        var right = ErrorWith(new Dictionary<string, object?> { ["correlationId"] = "abc" });

        // Act
        var leftToRight = left.Equals(right);
        var rightToLeft = right.Equals(left);

        // Assert
        leftToRight.ShouldBeFalse();
        rightToLeft.ShouldBeFalse();
    }

    [Fact]
    public void Equals_WhenJsonElementsParsedSeparatelyFromSameText_ReturnsTrue()
    {
        // Arrange
        using var leftDocument = JsonDocument.Parse("""{"a":[1,2]}""");
        using var rightDocument = JsonDocument.Parse("""{"a":[1,2]}""");
        var left = ErrorWith(new Dictionary<string, object?> { ["detail"] = leftDocument.RootElement });
        var right = ErrorWith(new Dictionary<string, object?> { ["detail"] = rightDocument.RootElement });

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Equals_WhenJsonElementsHaveDifferentContent_ReturnsFalse()
    {
        // Arrange
        using var leftDocument = JsonDocument.Parse("""{"a":[1,2]}""");
        using var rightDocument = JsonDocument.Parse("""{"a":[2,1]}""");
        var left = ErrorWith(new Dictionary<string, object?> { ["detail"] = leftDocument.RootElement });
        var right = ErrorWith(new Dictionary<string, object?> { ["detail"] = rightDocument.RootElement });

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void Equals_WhenBothMetadataEmptyOfDifferentTypes_ReturnsTrue()
    {
        // Arrange
        var left = ErrorWith(ErrorMetadata.Empty);
        var right = ErrorWith(new Dictionary<string, object?>());

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Equals_WhenBothMetadataAreEmptyImmutableDictionaries_ReturnsTrue()
    {
        // Arrange
        var left = ErrorWith(ErrorMetadata.Empty);
        var right = ErrorWith(ImmutableDictionary<string, object?>.Empty);

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeTrue();
    }

    [Fact]
    public void Equals_WhenMetadataEqualButCodeDiffers_ReturnsFalse()
    {
        // Arrange
        var left = ErrorWith(new Dictionary<string, object?> { ["a"] = 1L });
        var right = left with { Code = ErrorCode.From("OTHER.CODE") };

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeFalse();
    }

    // ── DeserializedValidationError ───────────────────────────────────────

    [Fact]
    public void ValidationEquals_WhenDistinctDictionariesHoldSameEntries_ReturnsTrue()
    {
        // Arrange
        var left = ValidationErrorWith(new Dictionary<string, object?> { ["attemptedValue"] = "x" });
        var right = ValidationErrorWith(new Dictionary<string, object?> { ["attemptedValue"] = "x" });

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeTrue();
        left.ShouldBe(right);
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void ValidationEquals_WhenSameKeyHasDifferentValue_ReturnsFalse()
    {
        // Arrange
        var left = ValidationErrorWith(new Dictionary<string, object?> { ["attemptedValue"] = "x" });
        var right = ValidationErrorWith(new Dictionary<string, object?> { ["attemptedValue"] = "y" });

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void ValidationEquals_WhenOneSideHasMissingKey_ReturnsFalse()
    {
        // Arrange
        var left = ValidationErrorWith(new Dictionary<string, object?> { ["a"] = 1L, ["b"] = 2L });
        var right = ValidationErrorWith(new Dictionary<string, object?> { ["a"] = 1L });

        // Act
        var leftToRight = left.Equals(right);
        var rightToLeft = right.Equals(left);

        // Assert
        leftToRight.ShouldBeFalse();
        rightToLeft.ShouldBeFalse();
    }

    [Fact]
    public void ValidationEquals_WhenBothValuesAreNull_ReturnsTrue()
    {
        // Arrange
        var left = ValidationErrorWith(new Dictionary<string, object?> { ["attemptedValue"] = null });
        var right = ValidationErrorWith(new Dictionary<string, object?> { ["attemptedValue"] = null });

        // Act
        var equal = left.Equals(right);

        // Assert
        equal.ShouldBeTrue();
    }

    [Fact]
    public void ValidationEquals_WhenJsonElementsParsedSeparatelyFromSameText_ReturnsTrue()
    {
        // Arrange
        using var leftDocument = JsonDocument.Parse("""{"a":[1,2]}""");
        using var rightDocument = JsonDocument.Parse("""{"a":[1,2]}""");
        var left = ValidationErrorWith(new Dictionary<string, object?> { ["detail"] = leftDocument.RootElement });
        var right = ValidationErrorWith(new Dictionary<string, object?> { ["detail"] = rightDocument.RootElement });

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void ValidationEquals_WhenJsonElementsHaveDifferentContent_ReturnsFalse()
    {
        // Arrange
        using var leftDocument = JsonDocument.Parse("""{"a":[1,2]}""");
        using var rightDocument = JsonDocument.Parse("""{"a":[1,3]}""");
        var left = ValidationErrorWith(new Dictionary<string, object?> { ["detail"] = leftDocument.RootElement });
        var right = ValidationErrorWith(new Dictionary<string, object?> { ["detail"] = rightDocument.RootElement });

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void ValidationEquals_WhenBothMetadataEmptyOfDifferentTypes_ReturnsTrue()
    {
        // Arrange
        var left = ValidationErrorWith(ErrorMetadata.Empty);
        var right = ValidationErrorWith(new Dictionary<string, object?>());

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void ValidationEquals_WhenMetadataEqualButPropertyNameDiffers_ReturnsFalse()
    {
        // Arrange
        var left = ValidationErrorWith(new Dictionary<string, object?> { ["a"] = 1L });
        var right = left with { PropertyName = "Phone" };

        // Act
        var equal = left == right;

        // Assert
        equal.ShouldBeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static DeserializedError ErrorWith(IReadOnlyDictionary<string, object?> metadata) =>
        new(ErrorCode.From("TEST.NOT_FOUND"), "Item 7 not found", ErrorCategory.NotFound, ErrorSeverity.Error, metadata);

    private static DeserializedValidationError ValidationErrorWith(IReadOnlyDictionary<string, object?> metadata) =>
        new(ErrorCode.From("VALIDATION.EMAIL"), "Email is required.", ErrorCategory.Validation, ErrorSeverity.Warning,
            metadata, "Email");

    [Fact]
    public void Equals_WhenDictionariesUseDifferentKeyComparers_IsSymmetric()
    {
        // Arrange: the public constructor accepts any dictionary
        var ignoreCase = new DeserializedError(
            ErrorCode.From("TEST.CODE"), "m", ErrorCategory.NotFound, ErrorSeverity.Error,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1L });
        var ordinal = new DeserializedError(
            ErrorCode.From("TEST.CODE"), "m", ErrorCategory.NotFound, ErrorSeverity.Error,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = 1L });

        // Act
        var forward = ignoreCase.Equals(ordinal);
        var backward = ordinal.Equals(ignoreCase);

        // Assert
        forward.ShouldBe(backward);
    }

    [Fact]
    public void GetHashCode_WhenEqualThroughAnIgnoreCaseComparer_IsEqual()
    {
        // Arrange
        var upper = new DeserializedError(
            ErrorCode.From("TEST.CODE"), "m", ErrorCategory.NotFound, ErrorSeverity.Error,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1L });
        var lower = new DeserializedError(
            ErrorCode.From("TEST.CODE"), "m", ErrorCategory.NotFound, ErrorSeverity.Error,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["a"] = 1L });

        // Act
        var equal = upper.Equals(lower);

        // Assert: equal objects hash alike
        if (equal)
            upper.GetHashCode().ShouldBe(lower.GetHashCode());
        else
            lower.Equals(upper).ShouldBeFalse();
    }
}
