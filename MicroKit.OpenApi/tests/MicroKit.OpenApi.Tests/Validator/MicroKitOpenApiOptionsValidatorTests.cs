using MicroKit.OpenApi.Options;
using MicroKit.OpenApi.Validator;
using Microsoft.Extensions.Options;
using Xunit;

namespace MicroKit.OpenApi.Tests.Validator;

public sealed class MicroKitOpenApiOptionsValidatorTests
{
    private static MicroKitOpenApiOptionsValidator CreateValidator() => new();

    private static MicroKitOpenApiOptions ValidOptions() => new()
    {
        Title = "Test API",
        DefaultVersion = "1.0",
        SupportedVersions = ["1.0"]
    };

    [Fact]
    public void Valid_Options_Pass()
    {
        var result = CreateValidator().Validate(null, ValidOptions());
        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Empty_Title_Fails()
    {
        var options = ValidOptions();
        options.Title = "";

        var result = CreateValidator().Validate(null, options);

        Assert.NotEqual(ValidateOptionsResult.Success, result);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Whitespace_Title_Fails()
    {
        var options = ValidOptions();
        options.Title = "   ";

        var result = CreateValidator().Validate(null, options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Empty_DefaultVersion_Fails()
    {
        var options = ValidOptions();
        options.DefaultVersion = "";

        var result = CreateValidator().Validate(null, options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void No_Versions_At_All_Fails()
    {
        var options = new MicroKitOpenApiOptions
        {
            Title = "Test API",
            DefaultVersion = "1.0",
            SupportedVersions = [],
            DeprecatedVersions = []
        };

        var result = CreateValidator().Validate(null, options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void DefaultVersion_Not_In_SupportedOrDeprecated_Fails()
    {
        var options = new MicroKitOpenApiOptions
        {
            Title = "Test API",
            DefaultVersion = "3.0",
            SupportedVersions = ["1.0"],
            DeprecatedVersions = []
        };

        var result = CreateValidator().Validate(null, options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void DefaultVersion_In_Deprecated_Passes()
    {
        var options = new MicroKitOpenApiOptions
        {
            Title = "Test API",
            DefaultVersion = "1.0",
            SupportedVersions = [],
            DeprecatedVersions = ["1.0"]
        };

        var result = CreateValidator().Validate(null, options);
        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Invalid_Contact_Email_Fails()
    {
        var options = ValidOptions();
        options.Contact = new ContactOptions { Email = "not-an-email" };

        var result = CreateValidator().Validate(null, options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Valid_Contact_Email_Passes()
    {
        var options = ValidOptions();
        options.Contact = new ContactOptions { Email = "support@example.com" };

        var result = CreateValidator().Validate(null, options);
        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void OAuth2_AuthorizationCode_Without_AuthorizationUrl_Fails()
    {
        var options = ValidOptions();
        options.Securities!.Add(new OAuth2SecurityOptions
        {
            SchemeName = "OAuth",
            FlowType = OAuth2FlowType.AuthorizationCode,
            AuthorizationUrl = null,
            TokenUrl = "https://auth.example.com/token"
        });

        var result = CreateValidator().Validate(null, options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void OAuth2_ClientCredentials_Without_TokenUrl_Fails()
    {
        var options = ValidOptions();
        options.Securities!.Add(new OAuth2SecurityOptions
        {
            SchemeName = "OAuth",
            FlowType = OAuth2FlowType.ClientCredentials,
            TokenUrl = null
        });

        var result = CreateValidator().Validate(null, options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void OAuth2_ClientCredentials_With_TokenUrl_Passes()
    {
        var options = ValidOptions();
        options.Securities!.Add(new OAuth2SecurityOptions
        {
            SchemeName = "OAuth",
            FlowType = OAuth2FlowType.ClientCredentials,
            TokenUrl = "https://auth.example.com/token"
        });

        var result = CreateValidator().Validate(null, options);
        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Duplicate_SchemeName_Fails()
    {
        var options = ValidOptions();
        options.Securities!.Add(new BearerSecurityOptions { SchemeName = "Bearer" });
        options.Securities.Add(new BearerSecurityOptions { SchemeName = "Bearer" });

        var result = CreateValidator().Validate(null, options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void ApiKey_Without_Name_Fails()
    {
        var options = ValidOptions();
        options.Securities!.Add(new ApiKeySecurityOptions { SchemeName = "ApiKey", Name = "" });

        var result = CreateValidator().Validate(null, options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Multiple_Failures_ReportedTogether()
    {
        var options = new MicroKitOpenApiOptions
        {
            Title = "",
            DefaultVersion = "",
            SupportedVersions = []
        };

        var result = CreateValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.NotNull(result.Failures);
        Assert.True(result.Failures!.Count() > 1);
    }
}
