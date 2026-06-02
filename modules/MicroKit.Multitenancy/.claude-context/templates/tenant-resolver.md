# Template: Tenant Resolution Strategy

## File: src/MicroKit.Multitenancy.AspNetCore/Strategies/{Name}TenantResolutionStrategy.cs

```csharp
using MicroKit.Multitenancy;
using MicroKit.Result;

namespace MicroKit.Multitenancy.AspNetCore;

/// <summary>
/// Resolves the current tenant from [source description].
/// </summary>
public sealed class {Name}TenantResolutionStrategy : ITenantResolutionStrategy
{
    /// <inheritdoc />
    public int Order => {n};

    /// <summary>Initializes a new instance of <see cref="{Name}TenantResolutionStrategy"/>.</summary>
    public {Name}TenantResolutionStrategy(/* inject dependencies */)
    {
    }

    /// <inheritdoc />
    public async ValueTask<Result<TenantId>> TryResolveAsync(CancellationToken ct = default)
    {
        // TODO: resolve TenantId from [source]
        // On failure: return Result<TenantId>.Failure(MultitenancyErrors.TenantNotFound)
        // Never throw — catch exceptions and return Result.Failure
        throw new NotImplementedException();
    }
}
```

## File: tests/MicroKit.Multitenancy.UnitTests/Strategies/{Name}TenantResolutionStrategyTests.cs

```csharp
namespace MicroKit.Multitenancy.UnitTests.Strategies;

public sealed class {Name}TenantResolutionStrategyTests
{
    [Fact]
    public async Task TryResolveAsync_When{Source}Present_ReturnsTenantId()
    {
        // Arrange
        var sut = new {Name}TenantResolutionStrategy(/* deps */);

        // Act
        var result = await sut.TryResolveAsync();

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(default);
    }

    [Fact]
    public async Task TryResolveAsync_When{Source}Missing_ReturnsFailure()
    {
        // Arrange
        var sut = new {Name}TenantResolutionStrategy(/* deps */);

        // Act
        var result = await sut.TryResolveAsync();

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task TryResolveAsync_WhenInvalidFormat_ReturnsFailureNotThrows()
    {
        // Arrange
        var sut = new {Name}TenantResolutionStrategy(/* deps with invalid value */);

        // Act + Assert — must not throw
        var result = await sut.TryResolveAsync();
        result.IsFailure.ShouldBeTrue();
    }
}
```
