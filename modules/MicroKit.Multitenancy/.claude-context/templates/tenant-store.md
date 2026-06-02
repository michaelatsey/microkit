# Template: Tenant Store

## File: src/MicroKit.Multitenancy/Stores/{Name}TenantStore.cs

```csharp
using MicroKit.Multitenancy;
using MicroKit.Result;

namespace MicroKit.Multitenancy;

/// <summary>
/// Tenant store backed by [backing description].
/// </summary>
public sealed class {Name}TenantStore : ITenantStore
{
    /// <summary>Initializes a new instance of <see cref="{Name}TenantStore"/>.</summary>
    public {Name}TenantStore(/* inject dependencies */)
    {
    }

    /// <inheritdoc />
    public async ValueTask<Result<ITenantInfo>> FindAsync(TenantId tenantId, CancellationToken ct = default)
    {
        // TODO: lookup tenant by id
        // On not found: return Result<ITenantInfo>.Failure(MultitenancyErrors.TenantNotFound)
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ITenantInfo>> ListAllAsync(CancellationToken ct = default)
    {
        // TODO: return all registered tenants
        throw new NotImplementedException();
    }
}
```

## File: tests/MicroKit.Multitenancy.UnitTests/Stores/{Name}TenantStoreTests.cs

```csharp
namespace MicroKit.Multitenancy.UnitTests.Stores;

public sealed class {Name}TenantStoreTests
{
    [Fact]
    public async Task FindAsync_WhenTenantExists_ReturnsTenantInfo()
    {
        // Arrange
        var sut = new {Name}TenantStore(/* deps */);
        var id = TenantId.NewId();

        // Act
        var result = await sut.FindAsync(id);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(id);
    }

    [Fact]
    public async Task FindAsync_WhenTenantNotFound_ReturnsFailure()
    {
        // Arrange
        var sut = new {Name}TenantStore(/* deps */);

        // Act
        var result = await sut.FindAsync(TenantId.NewId());

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task ListAllAsync_ReturnsTenants()
    {
        // Arrange
        var sut = new {Name}TenantStore(/* deps with 2 tenants */);

        // Act
        var tenants = await sut.ListAllAsync();

        // Assert
        tenants.ShouldNotBeEmpty();
    }
}
```
