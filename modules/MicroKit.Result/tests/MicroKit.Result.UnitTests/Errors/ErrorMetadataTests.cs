namespace MicroKit.Result.UnitTests.Errors;

public sealed class ErrorMetadataTests
{
    [Fact]
    public void Empty_WhenRead_IsEmptyAndShared()
    {
        // Arrange + Act
        var first = ErrorMetadata.Empty;
        var second = ErrorMetadata.Empty;

        // Assert
        first.ShouldBeEmpty();
        first.ShouldBeSameAs(second);
    }

    [Fact]
    public void Create_WhenSinglePair_ContainsOnlyThatPair()
    {
        // Arrange
        const string key = "userId";

        // Act
        var metadata = ErrorMetadata.Create(key, 42);

        // Assert
        metadata.Count.ShouldBe(1);
        metadata[key].ShouldBe(42);
    }

    [Fact]
    public void Create_WhenSinglePairWithNullValue_ContainsNullValue()
    {
        // Arrange
        const string key = "correlationId";

        // Act
        var metadata = ErrorMetadata.Create(key, null);

        // Assert
        metadata.ContainsKey(key).ShouldBeTrue();
        metadata[key].ShouldBeNull();
    }

    [Fact]
    public void Create_WhenSingleKeyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        string? key = null;

        // Act + Assert
        Should.Throw<ArgumentNullException>(() => ErrorMetadata.Create(key!, 1));
    }

    [Fact]
    public void Create_WhenSeveralPairs_ContainsAllPairs()
    {
        // Arrange
        var first = new KeyValuePair<string, object?>("a", 1);
        var second = new KeyValuePair<string, object?>("b", "two");

        // Act
        var metadata = ErrorMetadata.Create(first, second);

        // Assert
        metadata.Count.ShouldBe(2);
        metadata["a"].ShouldBe(1);
        metadata["b"].ShouldBe("two");
    }

    [Fact]
    public void Create_WhenDuplicateKey_LaterPairWins()
    {
        // Arrange
        var first = new KeyValuePair<string, object?>("key", "first");
        var second = new KeyValuePair<string, object?>("key", "second");

        // Act
        var metadata = ErrorMetadata.Create(first, second);

        // Assert
        metadata.Count.ShouldBe(1);
        metadata["key"].ShouldBe("second");
    }

    [Fact]
    public void Create_WhenNoPairs_ReturnsEmpty()
    {
        // Arrange
        ReadOnlySpan<KeyValuePair<string, object?>> pairs = [];

        // Act
        var metadata = ErrorMetadata.Create(pairs);

        // Assert
        metadata.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WhenPairKeyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var pair = new KeyValuePair<string, object?>(null!, 1);

        // Act + Assert
        Should.Throw<ArgumentNullException>(() => ErrorMetadata.Create(pair, pair));
    }
}
