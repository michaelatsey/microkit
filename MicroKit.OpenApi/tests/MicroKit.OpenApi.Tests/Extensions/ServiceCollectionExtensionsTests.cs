using MicroKit.OpenApi.Abstractions;
using MicroKit.OpenApi.Extensions;
using MicroKit.OpenApi.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MicroKit.OpenApi.Tests.Extensions;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMicroKitOpenApi_WithOptions_ReturnsBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddMicroKitOpenApi(options =>
        {
            options.Title = "Test API";
        });

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<IMicroKitOpenApiBuilder>(builder);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddMicroKitOpenApi_WithNullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentNullException>(() =>
            services.AddMicroKitOpenApi((Action<MicroKitOpenApiOptions>)null!));

        Assert.Equal("configure", ex.ParamName);
    }

    [Fact]
    public void AddMicroKitOpenApi_RegistersOptions()
    {
        var services = new ServiceCollection();
        services.AddMicroKitOpenApi(options =>
        {
            options.Title = "My API";
        });

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<MicroKitOpenApiOptions>>();

        Assert.Equal("My API", opts.Value.Title);
    }

    [Fact]
    public void AddMicroKitOpenApi_WithConfiguration_BindsSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MicroKit:OpenApi:Title"] = "Config Title",
                ["MicroKit:OpenApi:DefaultVersion"] = "1.0",
                ["MicroKit:OpenApi:SupportedVersions:0"] = "1.0"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMicroKitOpenApi(config);

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<MicroKitOpenApiOptions>>();

        Assert.Equal("Config Title", opts.Value.Title);
    }

    [Fact]
    public void AddMicroKitOpenApi_CodeOverride_WinsOverConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MicroKit:OpenApi:Title"] = "Config Title",
                ["MicroKit:OpenApi:DefaultVersion"] = "1.0",
                ["MicroKit:OpenApi:SupportedVersions:0"] = "1.0"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMicroKitOpenApi(config, options => options.Title = "Code Title");

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<MicroKitOpenApiOptions>>();

        Assert.Equal("Code Title", opts.Value.Title);
    }

    [Fact]
    public void AddBearerSecurity_AddsSchemeToOptions()
    {
        var services = new ServiceCollection();
        services.AddMicroKitOpenApi(options =>
        {
            options.Title = "Test API";
            options.SupportedVersions = ["1.0"];
        })
        .AddBearerSecurity();

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<MicroKitOpenApiOptions>>();

        Assert.NotNull(opts.Value.Securities);
        Assert.Contains(opts.Value.Securities!, s => s.SchemeName == "Bearer");
    }

    [Fact]
    public void AddApiKeySecurity_AddsSchemeToOptions()
    {
        var services = new ServiceCollection();
        services.AddMicroKitOpenApi(options =>
        {
            options.Title = "Test API";
            options.SupportedVersions = ["1.0"];
        })
        .AddApiKeySecurity();

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<MicroKitOpenApiOptions>>();

        Assert.Contains(opts.Value.Securities!, s => s.SchemeName == "ApiKey");
    }

    [Fact]
    public void AddBearerSecurity_TwiceSameName_DoesNotDuplicate()
    {
        var services = new ServiceCollection();
        services.AddMicroKitOpenApi(options =>
        {
            options.Title = "Test API";
            options.SupportedVersions = ["1.0"];
        })
        .AddBearerSecurity()
        .AddBearerSecurity();

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<MicroKitOpenApiOptions>>();

        var bearerSchemes = opts.Value.Securities!.Where(s => s.SchemeName == "Bearer").ToList();
        Assert.Single(bearerSchemes);
    }

    [Fact]
    public void AddServer_AddsToOptions()
    {
        var services = new ServiceCollection();
        services.AddMicroKitOpenApi(options =>
        {
            options.Title = "Test API";
            options.SupportedVersions = ["1.0"];
        })
        .AddServer("https://api.example.com", "Production");

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<MicroKitOpenApiOptions>>();

        Assert.Contains(opts.Value.Servers, s => s.Url == "https://api.example.com");
    }

    [Fact]
    public void AddVersion_AddsToSupportedVersions()
    {
        var services = new ServiceCollection();
        services.AddMicroKitOpenApi(options =>
        {
            options.Title = "Test API";
            options.SupportedVersions = ["1.0"];
        })
        .AddVersion("2.0");

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<MicroKitOpenApiOptions>>();

        Assert.Contains("2.0", opts.Value.SupportedVersions);
    }

    [Fact]
    public void AddVersion_Deprecated_AddsToDeprecatedVersions()
    {
        var services = new ServiceCollection();
        services.AddMicroKitOpenApi(options =>
        {
            options.Title = "Test API";
            options.SupportedVersions = ["1.0"];
        })
        .AddVersion("0.9", deprecated: true);

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<MicroKitOpenApiOptions>>();

        Assert.Contains("0.9", opts.Value.DeprecatedVersions);
    }

    [Fact]
    public void WithVersionDocuments_RegistersDocuments()
    {
        var services = new ServiceCollection();
        services.AddMicroKitOpenApi(options =>
        {
            options.Title = "Test API";
            options.SupportedVersions = ["1.0"];
        })
        .WithVersionDocuments("1.0", "2.0");

        Assert.NotNull(services);
    }
}
