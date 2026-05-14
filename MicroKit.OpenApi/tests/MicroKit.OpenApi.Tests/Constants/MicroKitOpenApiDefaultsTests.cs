using MicroKit.OpenApi.Constants;
using Xunit;

namespace MicroKit.OpenApi.Tests.Constants;

public sealed class MicroKitOpenApiDefaultsTests
{
    [Fact]
    public void ConfigurationSectionName_IsExpected()
    {
        Assert.Equal("MicroKit:OpenApi", MicroKitOpenApiDefaults.ConfigurationSectionName);
    }

    [Fact]
    public void DefaultApiVersion_Is1_0()
    {
        Assert.Equal("1.0", MicroKitOpenApiDefaults.DefaultApiVersion);
    }

    [Fact]
    public void DefaultApiVersionHeaderKey_IsExpected()
    {
        Assert.Equal("X-Api-Version", MicroKitOpenApiDefaults.DefaultApiVersionHeaderKey);
    }

    [Fact]
    public void DefaultApiVersionQueryKey_IsExpected()
    {
        Assert.Equal("api-version", MicroKitOpenApiDefaults.DefaultApiVersionQueryKey);
    }

    [Fact]
    public void DefaultScalarEndpointPath_ContainsDocumentName()
    {
        Assert.Contains("{documentName}", MicroKitOpenApiDefaults.DefaultScalarEndpointPath);
    }

    [Fact]
    public void DefaultOpenApiEndpointPath_ContainsDocumentName()
    {
        Assert.Contains("{documentName}", MicroKitOpenApiDefaults.DefaultOpenApiEndpointPath);
    }
}
