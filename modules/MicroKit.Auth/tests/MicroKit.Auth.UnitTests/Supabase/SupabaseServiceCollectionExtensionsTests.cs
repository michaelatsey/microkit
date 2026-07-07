using MicroKit.Auth.Supabase;

namespace MicroKit.Auth.UnitTests.Supabase;

/// <summary>
/// Registration/consumption tests for <c>AddMicroKitAuthSupabase</c> that exercise the public API the
/// way an ASP.NET Core host does — configuring options through the <c>Action&lt;SupabaseAuthOptions&gt;</c>
/// delegate. The delegate property assignments in these tests are the regression guard for the CS8852
/// bug (init-only options unconfigurable via the delegate): if the options accessors ever revert to
/// <c>init</c>, this file fails to compile and blocks CI.
/// </summary>
public sealed class SupabaseServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMicroKitAuthSupabase_ConfiguresOptionsViaLambda_ResolvesConfiguredOptions()
    {
        var services = new ServiceCollection();

        services.AddMicroKitAuthSupabase(o =>
        {
            o.ProjectUrl = "https://xyz.supabase.co";
            o.Issuer = "https://xyz.supabase.co/auth/v1";
            o.Audience = "authenticated";
            o.JwksCacheDuration = TimeSpan.FromMinutes(30);
            o.KeyRotationCooldown = TimeSpan.FromMinutes(2);
        });

        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<SupabaseAuthOptions>();
        options.ProjectUrl.ShouldBe("https://xyz.supabase.co");
        options.Issuer.ShouldBe("https://xyz.supabase.co/auth/v1");
        options.Audience.ShouldBe("authenticated");
        options.JwksCacheDuration.ShouldBe(TimeSpan.FromMinutes(30));
        options.KeyRotationCooldown.ShouldBe(TimeSpan.FromMinutes(2));
        options.JwksUri.ToString().ShouldBe("https://xyz.supabase.co/auth/v1/.well-known/jwks.json");

        sp.GetRequiredService<IJwtValidator>().ShouldBeOfType<SupabaseJwtValidator>();
        sp.GetRequiredService<IClaimsMapper>().ShouldBeOfType<SupabaseClaimsMapper>();
    }

    [Fact]
    public void AddMicroKitAuthSupabase_WhenProjectUrlMissing_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var ex = Should.Throw<InvalidOperationException>(
            () => services.AddMicroKitAuthSupabase(o => o.Issuer = "https://xyz.supabase.co/auth/v1"));

        ex.Message.ShouldContain("ProjectUrl");
    }
}
