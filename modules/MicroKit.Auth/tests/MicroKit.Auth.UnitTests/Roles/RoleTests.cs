namespace MicroKit.Auth.UnitTests.Roles;

public sealed class RoleTests
{
    [Fact]
    public void Of_WithValidName_CreatesRole()
    {
        var role = Role.Of("admin");

        role.Name.ShouldBe("admin");
    }

    [Fact]
    public void Of_WhenNameIsNull_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => Role.Of(null!));
    }

    [Fact]
    public void Of_WhenNameIsWhitespace_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => Role.Of("   "));
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        var role = Role.Of("auditor");

        role.ToString().ShouldBe("auditor");
    }

    [Fact]
    public void Equality_WhenSameName_AreEqual()
    {
        var role1 = Role.Of("admin");
        var role2 = Role.Of("admin");

        role1.ShouldBe(role2);
        (role1 == role2).ShouldBeTrue();
    }

    [Fact]
    public void Equality_WhenDifferentName_AreNotEqual()
    {
        var role1 = Role.Of("admin");
        var role2 = Role.Of("viewer");

        role1.ShouldNotBe(role2);
        (role1 != role2).ShouldBeTrue();
    }
}
