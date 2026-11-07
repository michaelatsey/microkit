using System.Collections.Immutable;
using System.Diagnostics;

namespace MicroKit.Result.UnitTests.Errors;

public sealed class ErrorMetadataBuilderTests
{
    [Fact]
    public void Build_WhenNothingAdded_ReturnsEmpty()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var metadata = builder.Build();

        // Assert
        metadata.ShouldBeEmpty();
    }

    [Fact]
    public void WithTimestamp_WhenCalled_AddsCurrentUtcTime()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();
        var before = DateTimeOffset.UtcNow;

        // Act
        var metadata = builder.WithTimestamp().Build();
        var after = DateTimeOffset.UtcNow;

        // Assert
        var timestamp = metadata["timestamp"].ShouldBeOfType<DateTimeOffset>();
        timestamp.Offset.ShouldBe(TimeSpan.Zero);
        timestamp.ShouldBeGreaterThanOrEqualTo(before);
        timestamp.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public void WithCorrelationId_WhenValueGiven_AddsCorrelationId()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var metadata = builder.WithCorrelationId("corr-123").Build();

        // Assert
        metadata["correlationId"].ShouldBe("corr-123");
    }

    [Fact]
    public void WithCorrelationId_WhenNull_StoresExplicitNull()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var metadata = builder.WithCorrelationId(null).Build();

        // Assert
        metadata.ContainsKey("correlationId").ShouldBeTrue();
        metadata["correlationId"].ShouldBeNull();
    }

    [Fact]
    public void WithTraceId_WhenActivityStarted_AddsActivityTraceId()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();
        using var activity = new Activity("error-metadata-test")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();

        // Act
        var metadata = builder.WithTraceId().Build();

        // Assert
        metadata["traceId"].ShouldBe(activity.TraceId.ToString());
        metadata["traceId"].ShouldBeOfType<string>().Length.ShouldBe(32);
    }

    [Fact]
    public void WithTraceId_WhenNoActivity_StoresNull()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();
        var previous = Activity.Current;
        Activity.Current = null;

        try
        {
            // Act
            var metadata = builder.WithTraceId().Build();

            // Assert
            metadata.ContainsKey("traceId").ShouldBeTrue();
            metadata["traceId"].ShouldBeNull();
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public void Add_WhenKeyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => builder.Add(null!, 1));

        // Assert
        exception.ParamName.ShouldBe("key");
    }

    [Fact]
    public void Add_WhenKeyAlreadyPresent_OverwritesValue()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder().Add("key", 1);

        // Act
        var metadata = builder.Add("key", 2).Build();

        // Assert
        metadata.Count.ShouldBe(1);
        metadata["key"].ShouldBe(2);
    }

    [Fact]
    public void Add_WhenChained_ReturnsSameBuilder()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var returned = builder.Add("key", 1);

        // Assert
        returned.ShouldBeSameAs(builder);
        builder.WithTimestamp().ShouldBeSameAs(builder);
        builder.WithCorrelationId("c").ShouldBeSameAs(builder);
        builder.WithTraceId().ShouldBeSameAs(builder);
        builder.Merge(ErrorMetadata.Empty).ShouldBeSameAs(builder);
    }

    [Fact]
    public void Merge_WhenKeysOverlap_OtherValuesWinAndNewKeysAdded()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder().Add("shared", "mine").Add("own", 1);
        var other = ErrorMetadata.Create(
            new KeyValuePair<string, object?>("shared", "theirs"),
            new KeyValuePair<string, object?>("extra", true));

        // Act
        var metadata = builder.Merge(other).Build();

        // Assert
        metadata.Count.ShouldBe(3);
        metadata["shared"].ShouldBe("theirs");
        metadata["own"].ShouldBe(1);
        metadata["extra"].ShouldBe(true);
    }

    [Fact]
    public void Merge_WhenOtherIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder();

        // Act
        var exception = Should.Throw<ArgumentNullException>(() => builder.Merge(null!));

        // Assert
        exception.ParamName.ShouldBe("other");
    }

    [Fact]
    public void Build_WhenBuilderChangedAfterwards_KeepsBuiltSnapshot()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder().Add("first", 1);
        var snapshot = builder.Build();

        // Act
        var later = builder.Add("second", 2).Build();

        // Assert
        snapshot.Count.ShouldBe(1);
        snapshot.ContainsKey("second").ShouldBeFalse();
        later.Count.ShouldBe(2);
    }

    [Fact]
    public void ImplicitConversion_WhenBuilderGiven_ReturnsBuiltDictionary()
    {
        // Arrange
        var builder = new ErrorMetadataBuilder().Add("key", "value");

        // Act
        ImmutableDictionary<string, object?> metadata = builder;

        // Assert
        metadata.Count.ShouldBe(1);
        metadata["key"].ShouldBe("value");
    }

    [Fact]
    public void ImplicitConversion_WhenBuilderIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        ErrorMetadataBuilder? builder = null;

        // Act
        var exception = Should.Throw<ArgumentNullException>(
            () => (ImmutableDictionary<string, object?>)builder!);

        // Assert
        exception.ParamName.ShouldBe("builder");
    }

    [Fact]
    public void Build_WhenUsedAsErrorMetadata_IsReadableThroughError()
    {
        // Arrange
        var metadata = new ErrorMetadataBuilder().Add("userId", 7).Build();

        // Act
        IError error = new BuiltMetadataError(metadata);

        // Assert
        error.TryGetMetadata<int>("userId").ShouldBe(7);
    }

    private sealed record BuiltMetadataError(IReadOnlyDictionary<string, object?> Values)
        : Error(ErrorCode.From("TEST.BUILT"), "Built metadata error")
    {
        public override IReadOnlyDictionary<string, object?> Metadata => Values;
    }
}
