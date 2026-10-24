using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

public sealed class FullNameTests
{
    [Fact]
    public void Constructor_PaddedComponents_TrimsBoth()
    {
        var name = new FullName("  Ada ", " Lovelace  ");

        name.FirstName.ShouldBe("Ada");
        name.LastName.ShouldBe("Lovelace");
        name.DisplayName.ShouldBe("Ada Lovelace");
        name.ToString().ShouldBe("Ada Lovelace");
    }

    [Theory]
    [InlineData(null, "Lovelace", "FirstName")]
    [InlineData("  ", "Lovelace", "FirstName")]
    [InlineData("Ada", "", "LastName")]
    [InlineData("Ada", null, "LastName")]
    public void Constructor_EmptyComponent_ThrowsDomainExceptionNamingIt(string? first, string? last, string component)
    {
        Should.Throw<DomainException>(() => new FullName(first!, last!))
            .Message.ShouldBe($"{component} cannot be null or empty.");
    }

    [Fact]
    public void Equality_IsStructural_AfterTrimming()
    {
        FullName.Create("Ada", "Lovelace").ShouldBe(new FullName(" Ada", "Lovelace "));
        FullName.Create("Ada", "Lovelace").ShouldNotBe(new FullName("Ada", "Byron"));
    }
}
