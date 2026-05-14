using MicroKit.OpenApi.Constants;
using MicroKit.OpenApi.Options;
using Xunit;

namespace MicroKit.OpenApi.Tests.Options;

public sealed class MicroKitOpenApiOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new MicroKitOpenApiOptions();

        Assert.Equal("API", options.Title);
        Assert.Equal(MicroKitOpenApiDefaults.DefaultApiVersion, options.DefaultVersion);
        Assert.Single(options.SupportedVersions);
        Assert.Equal(MicroKitOpenApiDefaults.DefaultApiVersion, options.SupportedVersions[0]);
        Assert.Empty(options.DeprecatedVersions);
        Assert.Empty(options.Servers);
        Assert.NotNull(options.Securities);
        Assert.Empty(options.Securities!);
        Assert.True(options.EnableScalar);
        Assert.Equal(ScalarTheme.Default, options.Theme);
    }

    [Fact]
    public void VersionLists_AreMutable()
    {
        var options = new MicroKitOpenApiOptions();
        options.SupportedVersions.Add("2.0");
        options.DeprecatedVersions.Add("0.9");

        Assert.Contains("2.0", options.SupportedVersions);
        Assert.Contains("0.9", options.DeprecatedVersions);
    }

    [Fact]
    public void Securities_CanAcceptMultipleSchemes()
    {
        var options = new MicroKitOpenApiOptions();
        options.Securities!.Add(new BearerSecurityOptions());
        options.Securities.Add(new ApiKeySecurityOptions());

        Assert.Equal(2, options.Securities.Count);
    }

    [Fact]
    public void Servers_CanBeAdded()
    {
        var options = new MicroKitOpenApiOptions();
        options.Servers.Add(new ServerOptions { Url = "https://api.example.com", Description = "Production" });

        Assert.Single(options.Servers);
        Assert.Equal("https://api.example.com", options.Servers[0].Url);
    }
}
