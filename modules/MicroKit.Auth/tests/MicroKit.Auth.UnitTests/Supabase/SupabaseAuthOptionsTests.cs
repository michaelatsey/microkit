using MicroKit.Auth.Supabase;

namespace MicroKit.Auth.UnitTests.Supabase;

public sealed class SupabaseAuthOptionsTests
{
    [Fact]
    public void JwksUri_DerivesCorrectSupabaseUrl()
    {
        var options = new SupabaseAuthOptions
        {
            ProjectUrl = "https://xyz.supabase.co",
            Issuer = "https://xyz.supabase.co/auth/v1"
        };

        options.JwksUri.ToString().ShouldBe("https://xyz.supabase.co/auth/v1/.well-known/jwks.json");
    }

    [Fact]
    public void JwksUri_StripsTrailingSlashFromProjectUrl()
    {
        var options = new SupabaseAuthOptions
        {
            ProjectUrl = "https://xyz.supabase.co/",
            Issuer = "https://xyz.supabase.co/auth/v1"
        };

        options.JwksUri.ToString().ShouldBe("https://xyz.supabase.co/auth/v1/.well-known/jwks.json");
    }

    [Fact]
    public void JwksUri_IsAbsoluteUri()
    {
        var options = new SupabaseAuthOptions
        {
            ProjectUrl = "https://xyz.supabase.co",
            Issuer = "https://xyz.supabase.co/auth/v1"
        };

        options.JwksUri.IsAbsoluteUri.ShouldBeTrue();
    }

    [Fact]
    public void DefaultAudience_IsAuthenticated()
    {
        var options = new SupabaseAuthOptions
        {
            ProjectUrl = "https://xyz.supabase.co",
            Issuer = "https://xyz.supabase.co/auth/v1"
        };

        options.Audience.ShouldBe("authenticated");
    }
}
