using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MicroKit.Logging.GeneratorTests;

public sealed class LogScopeGeneratorTests
{
    // Metadata references shared across all tests
    private static readonly IReadOnlyList<MetadataReference> BaseReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.Runtime.dll")),
        MetadataReference.CreateFromFile(typeof(MicroKit.Logging.MicroKitLogScopeAttribute).Assembly.Location),
    ];

    private static (GeneratorDriverRunResult result, CSharpCompilation compilation) RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "GeneratorTestAssembly",
            [syntaxTree],
            BaseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new LogScopeGenerator());
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        // Apply generated sources back to get the updated compilation for compile tests
        var updatedCompilation = compilation;
        foreach (var tree in result.GeneratedTrees)
            updatedCompilation = updatedCompilation.AddSyntaxTrees(tree);

        return (result, updatedCompilation);
    }

    [Fact]
    public void GeneratesBeginLogScope_ForDecoratedPartialClass()
    {
        var source = """
            using MicroKit.Logging;

            namespace MyApp;

            [MicroKitLogScope]
            public sealed partial class RequestScope
            {
                public string CorrelationId { get; init; } = "";
                public string RequestId { get; init; } = "";
            }
            """;

        var (result, _) = RunGenerator(source);

        result.GeneratedTrees.Should().HaveCount(1);
        var generated = result.GeneratedTrees[0].ToString();
        generated.Should().Contain("BeginLogScope");
        generated.Should().Contain("partial class RequestScope");
        generated.Should().Contain("new(\"CorrelationId\",");
        generated.Should().Contain("new(\"RequestId\",");
    }

    [Fact]
    public void GeneratesBeginLogScope_ForDecoratedPartialRecord()
    {
        var source = """
            using MicroKit.Logging;

            namespace MyApp;

            [MicroKitLogScope]
            public sealed partial record TenantScope(string TenantId, string UserId);
            """;

        var (result, _) = RunGenerator(source);

        result.GeneratedTrees.Should().HaveCount(1);
        var generated = result.GeneratedTrees[0].ToString();
        generated.Should().Contain("BeginLogScope");
        generated.Should().Contain("partial record TenantScope");
        generated.Should().Contain("new(\"TenantId\",");
        generated.Should().Contain("new(\"UserId\",");
    }

    [Fact]
    public void NullableProperties_CastToObjectInOutput()
    {
        var source = """
            using MicroKit.Logging;

            namespace MyApp;

            [MicroKitLogScope]
            public sealed partial class OperationScope
            {
                public string CorrelationId { get; init; } = "";
                public string? TenantId { get; init; }
            }
            """;

        var (result, _) = RunGenerator(source);

        result.GeneratedTrees.Should().HaveCount(1);
        var generated = result.GeneratedTrees[0].ToString();
        // Both properties use (object?) cast
        generated.Should().Contain("new(\"CorrelationId\", (object?)this.CorrelationId)");
        generated.Should().Contain("new(\"TenantId\", (object?)this.TenantId)");
    }

    [Fact]
    public void DoesNotGenerate_ForNonPartialClass()
    {
        var source = """
            using MicroKit.Logging;

            namespace MyApp;

            [MicroKitLogScope]
            public sealed class RequestScope
            {
                public string CorrelationId { get; init; } = "";
            }
            """;

        var (result, _) = RunGenerator(source);

        result.GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void DoesNotGenerate_ForClassWithoutAttribute()
    {
        var source = """
            namespace MyApp;

            public sealed partial class RequestScope
            {
                public string CorrelationId { get; init; } = "";
            }
            """;

        var (result, _) = RunGenerator(source);

        result.GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void StaticPropertiesAreExcluded()
    {
        var source = """
            using MicroKit.Logging;

            namespace MyApp;

            [MicroKitLogScope]
            public sealed partial class ServiceScope
            {
                public static string ServiceName { get; } = "MyService";
                public string CorrelationId { get; init; } = "";
            }
            """;

        var (result, _) = RunGenerator(source);

        result.GeneratedTrees.Should().HaveCount(1);
        var generated = result.GeneratedTrees[0].ToString();
        generated.Should().NotContain("ServiceName");
        generated.Should().Contain("new(\"CorrelationId\",");
    }

    [Fact]
    public void EmittedFile_HasCorrectHintName()
    {
        var source = """
            using MicroKit.Logging;

            namespace MyApp;

            [MicroKitLogScope]
            public sealed partial class RequestScope
            {
                public string CorrelationId { get; init; } = "";
            }
            """;

        var (result, _) = RunGenerator(source);

        result.GeneratedTrees.Should().HaveCount(1);
        var hintName = System.IO.Path.GetFileName(result.GeneratedTrees[0].FilePath);
        hintName.Should().Be("RequestScope.g.cs");
    }

    [Fact]
    public void EmittedSource_HasAutoGeneratedHeader()
    {
        var source = """
            using MicroKit.Logging;

            namespace MyApp;

            [MicroKitLogScope]
            public sealed partial class RequestScope
            {
                public string CorrelationId { get; init; } = "";
            }
            """;

        var (result, _) = RunGenerator(source);

        result.GeneratedTrees.Should().HaveCount(1);
        var generated = result.GeneratedTrees[0].ToString();
        generated.Should().StartWith("// <auto-generated/>");
        generated.Should().Contain("#nullable enable");
    }

    [Fact]
    public void Generator_ReportsNoDiagnostics()
    {
        var source = """
            using MicroKit.Logging;

            namespace MyApp;

            [MicroKitLogScope]
            public sealed partial class RequestScope
            {
                public string CorrelationId { get; init; } = "";
            }
            """;

        var (result, _) = RunGenerator(source);

        result.Diagnostics.Should().BeEmpty();
    }
}
