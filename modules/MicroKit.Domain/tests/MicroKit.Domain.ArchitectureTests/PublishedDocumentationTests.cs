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
    public void AggregateMutate_StatesTheEventRemovalInOneParagraph()
    {
        // Structure, not wording: the general paragraph says nothing of events, and exactly one
        // paragraph of the remarks is about the events of a failed mutation.
        var paragraphs = Documentation.Descendants("member")
            .Single(m => (string?)m.Attribute("name") == AggregateMutate)
            .Element("remarks")!
            .Elements("para")
            .Select(p => p.Value)
            .ToArray();

        paragraphs[0].ShouldNotContain("event", Case.Insensitive);
        paragraphs.Count(p => p.Contains("domain events", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
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

    // ADR-004 D1: a cache reads back what it wrote, so it is not a path that only writes.
    [Theory]
    [InlineData("T:MicroKit.Domain.ValueObjects.Common.Actor")]
    [InlineData("T:MicroKit.Domain.ValueObjects.Common.Json.ActorJsonConverter")]
    public void ActorJson_DoesNotPresentACacheAsAWriteOnlyPath(string type)
    {
        var doc = Member(type);

        doc.ShouldNotContain("or a cache written with default options never fails");
        doc.ShouldContain("only writes, such as an HTTP response or a log");
        doc.ShouldContain("a cache");
        doc.ShouldContain("reads with");
    }

    // ADR-004 D9: an OIDC sub may be longer than MaxLength; the documentation must not claim it fits.
    [Theory]
    [InlineData("T:MicroKit.Domain.ValueObjects.Common.Actor")]
    [InlineData("F:MicroKit.Domain.ValueObjects.Common.Actor.MaxLength")]
    public void Actor_StatesTheOidcSubjectLengthLimit(string member)
    {
        var doc = Member(member);

        doc.ShouldContain("255");
        doc.ShouldContain("ADR-004 D9");
    }

    [Fact]
    public void UtcInstant_StatesWhichInstantsItApplies()
    {
        // DateRange keeps its offset, so the rule is not applied to "every instant".
        var doc = Member("T:MicroKit.Domain.UtcInstant");

        doc.ShouldNotContain("every instant entering the domain");
        doc.ShouldContain("audit and event instants");
        doc.ShouldContain("DateRange");
    }

    // #182, ADR-004 D3: a plain aggregate keeps the events and changes of a method that throws.
    [Theory]
    [InlineData("T:MicroKit.Domain.Aggregates.AggregateRoot`1")]
    [InlineData("M:MicroKit.Domain.Aggregates.AggregateRoot`1.RaiseDomainEvent(MicroKit.Domain.Events.IDomainEvent)")]
    [InlineData("M:MicroKit.Domain.Aggregates.AuditableEntity`1.Mutate(System.DateTimeOffset,MicroKit.Domain.ValueObjects.Common.Actor,System.Action)")]
    [InlineData(AggregateMutate)]
    public void AggregateMembers_StateThatAnInstanceIsDiscardedAfterAThrow(string member)
    {
        var doc = Member(member);

        doc.ShouldContain("must be discarded, not saved");
        doc.ShouldContain("ADR-004 D3");
    }

    [Fact]
    public void AggregateRoot_StatesThatOnlyMutateRemovesTheEventsOfAFailedOperation()
    {
        var doc = Member("T:MicroKit.Domain.Aggregates.AggregateRoot`1");

        doc.ShouldContain("does not roll back the domain events it raised");
        doc.ShouldContain("AuditableAggregateRoot`1");
        doc.ShouldContain("Discard the change set that tracks it too");
    }

    // #182, ADR-004 D2: the closure and delegate a capturing Mutate lambda allocates.
    [Theory]
    [InlineData("AuditableEntity`1")]
    [InlineData("AuditableAggregateRoot`1")]
    public void Mutate_StatesItsAllocation(string type)
    {
        var doc = Member(
            $"M:MicroKit.Domain.Aggregates.{type}.Mutate(System.DateTimeOffset,MicroKit.Domain.ValueObjects.Common.Actor,System.Action)");

        doc.ShouldContain("allocates a closure and a delegate on each call");
        doc.ShouldContain("ADR-004 D2");
    }

    private static string Member(string name)
    {
        var member = Documentation.Descendants("member")
            .SingleOrDefault(m => (string?)m.Attribute("name") == name);

        member.ShouldNotBeNull($"no documentation for {name}");

        // The raw XML keeps cref targets; normalise the line breaks and indentation of the comment.
        return string.Join(' ', member.ToString().Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
    }
}
