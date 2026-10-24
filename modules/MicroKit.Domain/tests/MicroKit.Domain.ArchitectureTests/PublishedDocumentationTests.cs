using System.Xml.Linq;
using Shouldly;

namespace MicroKit.Domain.ArchitectureTests;

// The XML documentation file ships in the package and is what IntelliSense shows. These tests
// pin the parts of it that answer a known adopter question.
public sealed class PublishedDocumentationTests
{
    private static readonly XDocument Documentation =
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MicroKit.Domain.xml"));

    [Fact]
    public void EventRoot_DoesNotPresentARejectedContract()
    {
        var doc = Member("T:MicroKit.Domain.Events.IEvent");

        doc.ShouldContain("IDomainEvent");
        doc.ShouldContain("IIntegrationEvent");
        doc.ShouldNotContain("IApplicationEvent");
    }

    [Fact]
    public void EntityConstructor_ExplainsEfCoreConstructorBinding()
    {
        var doc = Member("M:MicroKit.Domain.Aggregates.Entity`1.#ctor(`0)");

        doc.ShouldContain("no parameterless constructor");
        doc.ShouldContain("EF Core calls a parameterised constructor");
        doc.ShouldContain("camelCase parameter binding a PascalCase property");
    }

    [Theory]
    [InlineData("AuditableEntity`1")]
    [InlineData("AuditableAggregateRoot`1")]
    public void AuditableConstructor_ExplainsHowToMaterialiseAnActor(string type)
    {
        var doc = Member(
            $"M:MicroKit.Domain.Aggregates.{type}.#ctor(`0,System.DateTimeOffset,MicroKit.Domain.ValueObjects.Common.Actor)");

        doc.ShouldContain("binds constructor parameters to mapped properties by name");
        doc.ShouldContain("owned or complex type");
        doc.ShouldContain("value converter");
        doc.ShouldContain("private constructor for materialisation");
    }

    private const string AggregateMutate =
        "M:MicroKit.Domain.Aggregates.AuditableAggregateRoot`1.Mutate(System.DateTimeOffset,MicroKit.Domain.ValueObjects.Common.Actor,System.Action)";

    [Fact]
    public void AggregateMutate_StatesTheEventRemovalOnce()
    {
        var doc = Member(AggregateMutate);

        doc.ShouldContain("Domain events raised inside a");
        Count(doc, "removed").ShouldBe(1);
    }

    [Theory]
    [InlineData("AuditableEntity`1")]
    [InlineData("AuditableAggregateRoot`1")]
    public void Mutate_StatesThatAFailedMutationKeepsChangesToOtherObjects(string type)
    {
        var doc = Member(
            $"M:MicroKit.Domain.Aggregates.{type}.Mutate(System.DateTimeOffset,MicroKit.Domain.ValueObjects.Common.Actor,System.Action)");

        doc.ShouldContain("on this object and on any other");
        doc.ShouldContain("keeps its audit stamp");
        doc.ShouldContain("before mutating any child");
    }

    private static int Count(string text, string word) =>
        text.Split(word, StringSplitOptions.None).Length - 1;

    private static string Member(string name)
    {
        var member = Documentation.Descendants("member")
            .SingleOrDefault(m => (string?)m.Attribute("name") == name);

        member.ShouldNotBeNull($"no documentation for {name}");

        // The raw XML keeps cref targets; normalise the line breaks and indentation of the comment.
        return string.Join(' ', member.ToString().Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
    }
}
