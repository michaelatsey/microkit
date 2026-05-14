using MicroKit.OpenApi.Options;
using Xunit;

namespace MicroKit.OpenApi.Tests.Options;

public sealed class SecurityOptionsTests
{
    [Fact]
    public void BearerSecurityOptions_Defaults_AreCorrect()
    {
        var options = new BearerSecurityOptions();

        Assert.Equal("Bearer", options.SchemeName);
        Assert.Equal(SecurityType.Bearer, options.Type);
        Assert.Equal("JWT", options.BearerFormat);
        Assert.Empty(options.Scopes);
        Assert.Null(options.PrefilledValue);
        Assert.Null(options.OpenIdConnectUrl);
        Assert.NotEmpty(options.Description);
    }

    [Fact]
    public void ApiKeySecurityOptions_Defaults_AreCorrect()
    {
        var options = new ApiKeySecurityOptions();

        Assert.Equal("ApiKey", options.SchemeName);
        Assert.Equal(SecurityType.ApiKey, options.Type);
        Assert.Equal("X-Api-Key", options.Name);
        Assert.Equal(ApiKeyLocation.Header, options.Location);
        Assert.Null(options.PrefilledValue);
    }

    [Fact]
    public void OAuth2SecurityOptions_Defaults_AreCorrect()
    {
        var options = new OAuth2SecurityOptions();

        Assert.Equal("OAuth2", options.SchemeName);
        Assert.Equal(SecurityType.OAuth2, options.Type);
        Assert.Equal(OAuth2FlowType.AuthorizationCode, options.FlowType);
        Assert.True(options.EnablePkce);
        Assert.Empty(options.Scopes);
        Assert.Empty(options.PreselectedScopes);
    }

    [Fact]
    public void BearerSecurityOptions_Type_ReturnsBearer()
    {
        SecuritySchemeOptions options = new BearerSecurityOptions();
        Assert.Equal(SecurityType.Bearer, options.Type);
    }

    [Fact]
    public void ApiKeySecurityOptions_Type_ReturnsApiKey()
    {
        SecuritySchemeOptions options = new ApiKeySecurityOptions();
        Assert.Equal(SecurityType.ApiKey, options.Type);
    }

    [Fact]
    public void OAuth2SecurityOptions_Type_ReturnsOAuth2()
    {
        SecuritySchemeOptions options = new OAuth2SecurityOptions();
        Assert.Equal(SecurityType.OAuth2, options.Type);
    }

    [Fact]
    public void BearerSecurityOptions_IsMutable()
    {
        var options = new BearerSecurityOptions { SchemeName = "MyBearer", BearerFormat = "JWT", PrefilledValue = "test-token" };

        Assert.Equal("MyBearer", options.SchemeName);
        Assert.Equal("test-token", options.PrefilledValue);
    }

    [Fact]
    public void ApiKeySecurityOptions_Location_CanBeChanged()
    {
        var options = new ApiKeySecurityOptions { Location = ApiKeyLocation.Query };
        Assert.Equal(ApiKeyLocation.Query, options.Location);
    }
}
