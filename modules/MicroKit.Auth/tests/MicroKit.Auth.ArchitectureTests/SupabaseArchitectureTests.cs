using System.Reflection;

namespace MicroKit.Auth.ArchitectureTests;

public sealed class SupabaseArchitectureTests
{
    private static readonly Assembly SupabaseAssembly = typeof(SupabaseJwtValidator).Assembly;

    [Fact]
    public void Supabase_ShouldNotDependOn_OtherFederationProviders()
    {
        // Provider packages must never reference each other — federation isolation rule
        var forbidden = new[]
        {
            "MicroKit.Auth.OpenIdConnect",
            "MicroKit.Auth.Cognito",
            "MicroKit.Auth.Keycloak",
            "MicroKit.Auth.Auth0",
            "MicroKit.Auth.EntraId",
            "MicroKit.Auth.IdentityServer",
        };

        var referencedNames = SupabaseAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        foreach (var provider in forbidden)
        {
            referencedNames.ShouldNotContain(provider);
        }
    }

    [Fact]
    public void Supabase_ShouldNotDependOn_Roles()
    {
        var rolesAssemblyName = typeof(RoleRegistry).Assembly.GetName().Name;
        var referencedNames = SupabaseAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(rolesAssemblyName);
    }

    [Fact]
    public void Supabase_ShouldNotDependOn_Permissions()
    {
        var permissionsAssemblyName = typeof(PermissionRegistry).Assembly.GetName().Name;
        var referencedNames = SupabaseAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referencedNames.ShouldNotContain(permissionsAssemblyName);
    }
}
