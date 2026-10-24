using MicroKit.Domain.Exceptions;
using MicroKit.Domain.ValueObjects.Common;
using Shouldly;
using Xunit;

namespace MicroKit.Domain.UnitTests.ValueObjects.Common;

public sealed class ActorTests
{
    [Theory]
    [InlineData("$system")]
    [InlineData("$anything")]
    [InlineData("  $system")]
    public void Constructor_ReservedPrefix_ThrowsDomainException(string value)
    {
        Should.Throw<DomainException>(() => new Actor(value));
    }

    [Fact]
    public void System_IsNotEqualToAUserNamedSystem()
    {
        (Actor.System != new Actor("system")).ShouldBeTrue();
        Actor.System.Equals(new Actor("system")).ShouldBeFalse();
    }

    [Fact]
    public void BuiltIns_CarryTheReservedPrefix()
    {
        Actor.System.Value.ShouldBe("$system");
        Actor.Anonymous.Value.ShouldBe("$anonymous");
        Actor.Migration.Value.ShouldBe("$migration");
    }

    [Fact]
    public void BuiltIns_AreDistinct()
    {
        Actor[] builtIns = [Actor.System, Actor.Anonymous, Actor.Migration];

        builtIns.Distinct().Count().ShouldBe(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_Empty_ThrowsDomainException(string? value)
    {
        Should.Throw<DomainException>(() => new Actor(value!));
    }

    [Fact]
    public void Constructor_TooLong_ThrowsDomainException()
    {
        Should.Throw<DomainException>(() => new Actor(new string('a', Actor.MaxLength + 1)));
    }

    [Fact]
    public void Constructor_MaxLength_IsAccepted()
    {
        new Actor(new string('a', Actor.MaxLength)).Value.Length.ShouldBe(Actor.MaxLength);
    }

    [Fact]
    public void Constructor_TrimsAndKeepsAnInnerDollar()
    {
        var actor = new Actor("  user$1  ");

        actor.Value.ShouldBe("user$1");
        actor.ToString().ShouldBe("user$1");
    }

    [Fact]
    public void Restore_BuiltInValues_ReturnTheBuiltIns()
    {
        Actor.Restore(Actor.System.Value).ShouldBeSameAs(Actor.System);
        Actor.Restore(Actor.Anonymous.Value).ShouldBeSameAs(Actor.Anonymous);
        Actor.Restore(Actor.Migration.Value).ShouldBeSameAs(Actor.Migration);
    }

    [Theory]
    [InlineData("$system   ")]
    [InlineData("  $system")]
    [InlineData("\t$system\r\n")]
    public void Restore_PaddedBuiltInValue_ReturnsTheBuiltIn(string stored)
    {
        // A fixed-width column pads the stored value; the constructor trims a user value the same way.
        Actor.Restore(stored).ShouldBeSameAs(Actor.System);
    }

    [Theory]
    [InlineData("alice\nINFO forged audit line")]
    [InlineData("alice\r\nbob")]
    [InlineData("alice\tbob")]
    [InlineData("alice\u0000")]
    [InlineData("ali\u200Bce")]   // zero-width space
    [InlineData("alice\u202E")]   // right-to-left override
    public void Constructor_ControlOrFormatCharacter_ThrowsDomainException(string value)
    {
        Should.Throw<DomainException>(() => new Actor(value))
            .Message.ShouldBe("Actor identifier cannot contain a control or format character.");
        Should.Throw<DomainException>(() => Actor.Restore(value));
    }

    [Theory]
    [InlineData("service account")]
    [InlineData("auth0|5f7c8ec7c33c6c004bbafe82")]
    [InlineData("Jos\u00E9")]
    public void Constructor_PrintableIdentifier_IsAccepted(string value)
    {
        new Actor(value).Value.ShouldBe(value);
    }

    [Fact]
    public void Restore_Null_ThrowsDomainException()
    {
        Should.Throw<DomainException>(() => Actor.Restore(null!));
    }

    [Fact]
    public void Restore_PaddedUserValue_ReturnsTheTrimmedActor()
    {
        Actor.Restore("alice   ").ShouldBe(new Actor("alice"));
    }

    [Fact]
    public void Restore_UserValue_RoundTrips()
    {
        var actor = new Actor("user-1");

        Actor.Restore(actor.Value).ShouldBe(actor);
        Actor.Restore("system").ShouldNotBe(Actor.System);
    }

    [Theory]
    [InlineData("$other")]
    [InlineData("$System")]
    [InlineData("")]
    public void Restore_UnknownReservedOrEmptyValue_ThrowsDomainException(string value)
    {
        Should.Throw<DomainException>(() => Actor.Restore(value));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        new Actor("user-1").ShouldBe(new Actor(" user-1 "));
    }
}
