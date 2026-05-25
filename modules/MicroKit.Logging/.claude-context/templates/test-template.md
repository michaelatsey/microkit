# Template: Test

Code template for xUnit test classes in MicroKit.Logging.

Used by `/generate-tests` command.

---

## File: `{TargetClass}Tests.cs`

```csharp
using FluentAssertions;
using MicroKit.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MicroKit.Logging.UnitTests.{SubNamespace};

/// <summary>
/// Unit tests for <see cref="{TargetClass}"/>.
/// </summary>
public sealed class {TargetClass}Tests
{
    // ── Dependencies ───────────────────────────────────────────────────────
    // Use NSubstitute for all mocks and stubs

    private readonly I{Dependency1} _{dependency1} = Substitute.For<I{Dependency1}>();
    private readonly {TargetClass} _sut;

    public {TargetClass}Tests()
    {
        _sut = new {TargetClass}(_{dependency1});
    }

    // ── {Method1} ──────────────────────────────────────────────────────────

    [Fact]
    public void {Method1}_When{Condition}_Should{ExpectedResult}()
    {
        // Arrange
        // Act
        // Assert — use FluentAssertions, never Assert.Equal
        // result.Should().Be(expected);
    }

    [Fact]
    public void {Method1}_When{NegativeCondition}_Should{NegativeResult}()
    {
        // Arrange
        // Act
        // Assert
    }

    // ── Async tests ────────────────────────────────────────────────────────
    // Use ValueTask where the SUT is ValueTask

    [Fact]
    public async Task {AsyncMethod}_When{Condition}_Should{ExpectedResult}()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _sut.{AsyncMethod}Async(cts.Token);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task {AsyncMethod}_WhenCancelled_ThrowsOperationCancelledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => _sut.{AsyncMethod}Async(cts.Token).AsTask();

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

---

## Architecture Test Template

```csharp
using NetArchTest.Rules;
using Xunit;

namespace MicroKit.Logging.ArchitectureTests;

public sealed class DependencyRuleTests
{
    [Fact]
    public void Abstractions_ShouldNot_HaveProjectReferences_ToCore()
    {
        var result = Types
            .InAssembly(typeof(MicroKit.Logging.Abstractions.ILogEnricher).Assembly)
            .ShouldNot()
            .HaveDependencyOn("MicroKit.Logging")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "MicroKit.Logging.Abstractions must not depend on MicroKit.Logging core");
    }

    [Fact]
    public void Core_ShouldNot_HaveDependency_OnSerilog()
    {
        var result = Types
            .InAssembly(typeof(MicroKit.Logging.EnrichmentPipeline).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Serilog")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "MicroKit.Logging core must not depend on Serilog");
    }
}
```
