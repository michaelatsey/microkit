namespace MicroKit.Auth.UnitTests;

public sealed class PermissionPolicyProviderTests
{
    private static PermissionPolicyProvider CreateSut()
    {
        var options = Options.Create(new AuthorizationOptions());
        return new PermissionPolicyProvider(options);
    }

    [Fact]
    public async Task GetPolicyAsync_WhenPermissionPolicyName_ReturnsPolicyWithRequirement()
    {
        var sut = CreateSut();
        var policyName = $"{PermissionPolicyProvider.PolicyPrefix}audits:read";

        var policy = await sut.GetPolicyAsync(policyName);

        policy.ShouldNotBeNull();
        var requirement = policy!.Requirements
            .OfType<PermissionAuthorizationRequirement>()
            .SingleOrDefault();
        requirement.ShouldNotBeNull();
        requirement!.Permission.ShouldBe(Permission.Of("audits", "read"));
    }

    [Fact]
    public async Task GetPolicyAsync_WhenUnknownPolicyName_ReturnsNull()
    {
        var sut = CreateSut();

        var policy = await sut.GetPolicyAsync("SomeOtherPolicy");

        policy.ShouldBeNull();
    }

    [Fact]
    public async Task GetPolicyAsync_WhenMalformedPermissionPolicyName_ReturnsNull()
    {
        var sut = CreateSut();
        // Missing action part
        var policyName = $"{PermissionPolicyProvider.PolicyPrefix}auditsonly";

        var policy = await sut.GetPolicyAsync(policyName);

        policy.ShouldBeNull();
    }

    [Fact]
    public async Task GetPolicyAsync_WhenPermissionPolicy_PolicyRequiresAuthenticatedUser()
    {
        var sut = CreateSut();
        var policyName = $"{PermissionPolicyProvider.PolicyPrefix}docs:read";

        var policy = await sut.GetPolicyAsync(policyName);

        policy.ShouldNotBeNull();
        // DenyAnonymousAuthorizationRequirement is added by RequireAuthenticatedUser()
        policy!.Requirements
            .OfType<DenyAnonymousAuthorizationRequirement>()
            .ShouldNotBeEmpty();
    }
}
