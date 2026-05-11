using MicroKit.Domain.Tests.Fakes;
using Xunit;

namespace MicroKit.Domain.Tests;

public sealed class EntityTests
{
    [Fact]
    public void Constructor_WithValidId_SetsId()
    {
        var id = Guid.NewGuid();
        var entity = new ProductEntity(id, "Widget");
        Assert.Equal(id, entity.Id);
    }

    [Fact]
    public void Constructor_WithDefaultId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ProductEntity(Guid.Empty, "Widget"));
    }

    [Fact]
    public void GetKeys_ReturnsArrayContainingId()
    {
        var id = Guid.NewGuid();
        var entity = new ProductEntity(id, "Widget");
        Assert.Equal([id], entity.GetKeys());
    }

    [Fact]
    public void ToString_ReturnsExpectedFormat()
    {
        var id = Guid.NewGuid();
        var entity = new ProductEntity(id, "Widget");
        Assert.Equal($"[ENTITY: ProductEntity] Id = {id}", entity.ToString());
    }

    [Fact]
    public void Id_HasNoPublicSetter_ButHasPrivateSetter()
    {
        // Inspect the declaring type directly — the closed generic Entity<Guid>
        var entityType = typeof(ProductEntity).BaseType!; // Entity<Guid>
        var prop = entityType.GetProperty("Id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(prop);
        // External code must not be able to set Id
        Assert.Null(prop!.GetSetMethod(nonPublic: false));
        // EF Core can still reach the private setter via reflection
        var privateSetter = prop.GetSetMethod(nonPublic: true);
        Assert.NotNull(privateSetter);
        Assert.True(privateSetter!.IsPrivate);
    }
}
