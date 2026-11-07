using System.Globalization;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using MicroKit.Result.UnitTests.TestSupport;

namespace MicroKit.Result.UnitTests.Properties;

// The five laws of .claude/rules/testing.md. Each property draws r from an int and a bool choosing
// its rail, so successes and failures are both generated; the Classify labels report the split
// in FsCheck's output ("Ok, passed 100 tests (x% success, y% failure)."). Failures carry a
// TestError built from the int: two results compare equal by the record's value equality.
public sealed class MonadLawTests
{
    private const int Cases = 100;

    [Property(MaxTest = Cases)]
    public Property Map_WithIdentity_ReturnsEqualResult(int seed, bool isSuccess)
    {
        // Arrange
        Result<int> r = Build(seed, isSuccess);

        // Act
        Result<int> mapped = r.Map(x => x);

        // Assert
        return Classified(mapped == r, isSuccess);
    }

    [Property(MaxTest = Cases)]
    public Property Map_WithComposedFunctions_EqualsSuccessiveMaps(int seed, bool isSuccess)
    {
        // Arrange
        Result<int> r = Build(seed, isSuccess);

        // Act
        Result<string> successive = r.Map(F).Map(G);
        Result<string> composed = r.Map(x => G(F(x)));

        // Assert
        return Classified(successive == composed, isSuccess);
    }

    [Property(MaxTest = Cases)]
    public Property Bind_WithSuccessOnLeft_EqualsFunctionApplied(int a)
    {
        // Act
        Result<long> bound = Result.Success(a).Bind(BindF);
        Result<long> applied = BindF(a);

        // Assert — the rail is the one BindF picks for a
        return (bound == applied).Classify(a >= 0, "success").Classify(a < 0, "failure");
    }

    [Property(MaxTest = Cases)]
    public Property Bind_WithSuccessOnRight_ReturnsEqualResult(int seed, bool isSuccess)
    {
        // Arrange
        Result<int> r = Build(seed, isSuccess);

        // Act
        Result<int> bound = r.Bind(x => Result.Success(x));

        // Assert
        return Classified(bound == r, isSuccess);
    }

    [Property(MaxTest = Cases)]
    public Property Bind_WithNestedFunctions_IsAssociative(int seed, bool isSuccess)
    {
        // Arrange
        Result<int> r = Build(seed, isSuccess);

        // Act
        Result<string> leftNested = r.Bind(BindF).Bind(BindG);
        Result<string> rightNested = r.Bind(x => BindF(x).Bind(BindG));

        // Assert
        return Classified(leftNested == rightNested, isSuccess);
    }

    private static Result<int> Build(int seed, bool isSuccess) =>
        isSuccess
            ? Result.Success(seed)
            : Result.Failure<int>(new TestError(seed.ToString(CultureInfo.InvariantCulture)));

    private static Property Classified(bool law, bool isSuccess) =>
        law.Classify(isSuccess, "success").Classify(!isSuccess, "failure");

    private static long F(int x) => ((long)x * 2) + 1;

    private static string G(long x) => (x - 3).ToString(CultureInfo.InvariantCulture);

    // Fails on negative inputs, so Bind's laws are checked across a rail change too.
    private static Result<long> BindF(int x) =>
        x < 0 ? Result.Failure<long>(new NotFoundTestError(x)) : Result.Success(F(x));

    // Fails on multiples of 3.
    private static Result<string> BindG(long x) =>
        x % 3 == 0 ? Result.Failure<string>(new OtherError()) : Result.Success(G(x));
}
