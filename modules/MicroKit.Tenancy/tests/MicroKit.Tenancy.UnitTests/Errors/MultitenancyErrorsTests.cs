using MicroKit.Tenancy;
using MicroKit.Result;
using Shouldly;
using Xunit;

namespace MicroKit.Tenancy.UnitTests.Errors;

public sealed class MultitenancyErrorsTests
{
    [Fact]
    public void TenantNotFound_HasExpectedCode()
    {
        MultitenancyErrors.TenantNotFound.Code.Value.ShouldBe("MULTITENANCY.TENANT.NOT_FOUND");
    }

    [Fact]
    public void InvalidTenantId_HasExpectedCode()
    {
        MultitenancyErrors.InvalidTenantId.Code.Value.ShouldBe("MULTITENANCY.TENANT.INVALID_ID");
    }

    [Fact]
    public void TenantInactive_HasExpectedCode()
    {
        MultitenancyErrors.TenantInactive.Code.Value.ShouldBe("MULTITENANCY.TENANT.INACTIVE");
    }

    [Fact]
    public void ResolutionFailed_HasExpectedCode()
    {
        MultitenancyErrors.ResolutionFailed.Code.Value.ShouldBe("MULTITENANCY.RESOLUTION.FAILED");
    }

    [Fact]
    public void AllErrors_HaveNonEmptyMessage()
    {
        Error[] errors =
        [
            MultitenancyErrors.TenantNotFound,
            MultitenancyErrors.InvalidTenantId,
            MultitenancyErrors.TenantInactive,
            MultitenancyErrors.ResolutionFailed,
        ];

        foreach (var error in errors)
            error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TenantNotFound_CategoryIsNotFound()
    {
        MultitenancyErrors.TenantNotFound.Category.ShouldBe(ErrorCategory.NotFound);
    }

    [Fact]
    public void TenantInactive_CategoryIsForbidden()
    {
        MultitenancyErrors.TenantInactive.Category.ShouldBe(ErrorCategory.Forbidden);
    }

    [Fact]
    public void InvalidTenantId_CategoryIsValidation()
    {
        MultitenancyErrors.InvalidTenantId.Category.ShouldBe(ErrorCategory.Validation);
    }

    [Fact]
    public void ResolutionFailed_CategoryIsNotFound()
    {
        MultitenancyErrors.ResolutionFailed.Category.ShouldBe(ErrorCategory.NotFound);
    }
}
