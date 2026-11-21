using Microsoft.Extensions.Time.Testing;

namespace MicroKit.Messaging.UnitTests.Processing;

/// <summary>
/// <see cref="InboxRetryPolicy"/>, shared by a failed delivery and a store's retry loop: the exact
/// back-off curve with the jitter neutralised, its cap, its clamp, and the jitter itself.
/// </summary>
public sealed class InboxRetryPolicyTests
{
    private static InboxRetryPolicy Policy(Random? random = null) => new(
        new InboxProcessorOptions { MaxRetries = 3, MaxRetryBackoff = TimeSpan.FromHours(1) },
        new FakeTimeProvider(GuardHarness.Now),
        random ?? FixedRandom.NoJitter);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(5, 32)]
    [InlineData(10, 1024)]
    public void ComputeBackoffCeiling_FollowsTheExponentialCurve(int attempts, int expectedSeconds)
        => Policy().ComputeBackoffCeiling(attempts).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));

    [Fact]
    public void ComputeBackoffCeiling_IsCappedAtMaxRetryBackoff()
        => Policy().ComputeBackoffCeiling(12).ShouldBe(TimeSpan.FromHours(1));

    [Fact]
    public void ComputeBackoffCeiling_ClampsACorruptedRetryCount()
    {
        Policy().ComputeBackoffCeiling(int.MaxValue).ShouldBe(TimeSpan.FromHours(1));
        Policy().ComputeBackoffCeiling(-5).ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ApplyJitter_DrawsOverTheWholeInterval()
    {
        Policy(FixedRandom.ZeroDelay).ApplyJitter(TimeSpan.FromSeconds(8)).ShouldBe(TimeSpan.Zero);
        Policy(new FixedRandom(0.5)).ApplyJitter(TimeSpan.FromSeconds(8)).ShouldBe(TimeSpan.FromSeconds(4));
        Policy().ApplyJitter(TimeSpan.FromSeconds(8)).ShouldBe(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void IsExhausted_AtMaxRetries()
    {
        Policy().IsExhausted(2).ShouldBeFalse();
        Policy().IsExhausted(3).ShouldBeTrue();
    }

    [Fact]
    public void NextAttemptAt_IsNowPlusTheJitteredCeiling()
        => Policy().NextAttemptAt(2).ShouldBe(GuardHarness.Now.AddSeconds(4));
}
