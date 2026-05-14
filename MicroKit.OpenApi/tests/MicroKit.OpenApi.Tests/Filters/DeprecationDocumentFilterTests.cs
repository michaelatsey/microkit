using MicroKit.OpenApi.Filters;
using Microsoft.OpenApi;
using Xunit;

namespace MicroKit.OpenApi.Tests.Filters;

public sealed class DeprecationDocumentFilterTests
{
    private static readonly IServiceProvider _services = new ServiceCollection().BuildServiceProvider();

    private static DocumentFilterContext MakeContext(bool isDeprecated, string documentName = "v1.0")
        => new()
        {
            DocumentName = documentName,
            ApiVersion = documentName,
            IsDeprecated = isDeprecated,
            ServiceProvider = _services
        };

    private static OpenApiDocument MakeDocument(string version = "v1.0", string? description = null)
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Version = version, Description = description },
            Paths = new OpenApiPaths()
        };

        var path = new OpenApiPathItem();
        path.Operations = new Dictionary<HttpMethod, OpenApiOperation>
        {
            [HttpMethod.Get] = new OpenApiOperation { Description = "Get resource" }
        };
        doc.Paths["/resource"] = path;

        return doc;
    }

    [Fact]
    public async Task NonDeprecated_Document_IsNotModified()
    {
        var filter = new DeprecationDocumentFilter();
        var doc = MakeDocument();
        var originalDescription = doc.Info!.Description;

        await filter.ApplyAsync(doc, MakeContext(isDeprecated: false));

        Assert.Equal(originalDescription, doc.Info.Description);
        foreach (var path in doc.Paths!.Values)
        {
            foreach (var op in path.Operations!.Values)
            {
                Assert.False(op.Deprecated);
            }
        }
    }

    [Fact]
    public async Task Deprecated_Document_MarksAllOperationsDeprecated()
    {
        var filter = new DeprecationDocumentFilter();
        var doc = MakeDocument();

        await filter.ApplyAsync(doc, MakeContext(isDeprecated: true));

        foreach (var path in doc.Paths!.Values)
        {
            foreach (var op in path.Operations!.Values)
            {
                Assert.True(op.Deprecated);
            }
        }
    }

    [Fact]
    public async Task Deprecated_Document_PrependsBannerToDescription()
    {
        var filter = new DeprecationDocumentFilter();
        var doc = MakeDocument(description: "Original description.");

        await filter.ApplyAsync(doc, MakeContext(isDeprecated: true));

        Assert.NotNull(doc.Info!.Description);
        Assert.StartsWith("⚠️ **DEPRECATED VERSION**", doc.Info.Description);
        Assert.Contains("Original description.", doc.Info.Description);
    }

    [Fact]
    public async Task Deprecated_Description_SetOnceRegardlessOfPathCount()
    {
        var filter = new DeprecationDocumentFilter();
        var doc = MakeDocument();

        // Second path — ensures the description banner appears exactly once.
        var path2 = new OpenApiPathItem();
        path2.Operations = new Dictionary<HttpMethod, OpenApiOperation>
        {
            [HttpMethod.Post] = new OpenApiOperation()
        };
        doc.Paths!["/other"] = path2;

        await filter.ApplyAsync(doc, MakeContext(isDeprecated: true));

        var banner = "⚠️ **DEPRECATED VERSION**";
        var occurrences = CountOccurrences(doc.Info!.Description ?? "", banner);
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public async Task Deprecated_Operation_PrependsBannerToOperationDescription()
    {
        var filter = new DeprecationDocumentFilter();
        var doc = MakeDocument();

        await filter.ApplyAsync(doc, MakeContext(isDeprecated: true));

        foreach (var path in doc.Paths!.Values)
        {
            foreach (var op in path.Operations!.Values)
            {
                Assert.Contains("deprecated API version", op.Description);
            }
        }
    }

    [Fact]
    public async Task Deprecated_NullInfo_DoesNotThrow()
    {
        var filter = new DeprecationDocumentFilter();
        var doc = new OpenApiDocument();
        doc.Info = null!;
        doc.Paths = new OpenApiPaths();

        var exception = await Record.ExceptionAsync(() =>
            filter.ApplyAsync(doc, MakeContext(isDeprecated: true)));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Deprecated_NullPaths_DoesNotThrow()
    {
        var filter = new DeprecationDocumentFilter();
        var doc = new OpenApiDocument { Info = new OpenApiInfo { Version = "v1" } };
        doc.Paths = null!;

        var exception = await Record.ExceptionAsync(() =>
            filter.ApplyAsync(doc, MakeContext(isDeprecated: true)));

        Assert.Null(exception);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
