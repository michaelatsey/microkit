namespace MicroKit.Result.UnitTests.Errors;

// #198 review: the core owns the definition of a fault of the system, of the input categories, and of the text a
// client reads instead of a fault's message.
public sealed class ErrorCategoryExtensionsTests
{
    [Theory]
    [InlineData(ErrorCategory.Technical, true)]
    [InlineData(ErrorCategory.External, true)]
    [InlineData(ErrorCategory.Unavailable, true)]
    [InlineData(ErrorCategory.Timeout, true)]
    [InlineData(ErrorCategory.NotSupported, true)]
    [InlineData((ErrorCategory)999, true)]
    [InlineData(ErrorCategory.NotFound, false)]
    [InlineData(ErrorCategory.Unauthorized, false)]
    [InlineData(ErrorCategory.Validation, false)]
    [InlineData(ErrorCategory.Cancelled, false)]
    public void IsSystemFault_ForEachCategory_MatchesTheFaultsOfTheSystem(ErrorCategory category, bool expected)
    {
        // Act
        var isFault = category.IsSystemFault();

        // Assert
        isFault.ShouldBe(expected);
    }

    [Theory]
    [InlineData(ErrorCategory.Validation, true)]
    [InlineData(ErrorCategory.BusinessRule, true)]
    [InlineData(ErrorCategory.NotFound, false)]
    [InlineData(ErrorCategory.Unauthorized, false)]
    [InlineData(ErrorCategory.Technical, false)]
    [InlineData((ErrorCategory)999, false)]
    public void DescribesInput_ForEachCategory_IsTrueOnlyForValidationAndBusinessRule(ErrorCategory category, bool expected)
    {
        // Act
        var describesInput = category.DescribesInput();

        // Assert
        describesInput.ShouldBe(expected);
    }

    [Fact]
    public void SystemFaultMessage_WhenRead_IsAPropertyNotACompileTimeConstant()
    {
        // Arrange: a const is copied into every consuming assembly at compile time
        var property = typeof(ErrorCategoryExtensions).GetProperty(nameof(ErrorCategoryExtensions.SystemFaultMessage));

        // Assert
        property.ShouldNotBeNull();
        ErrorCategoryExtensions.SystemFaultMessage.ShouldBe("The server could not complete the request.");
    }

    [Fact]
    public void Code_OfError_CannotBeOverridden()
    {
        // Arrange: a computed override would report a code that equality and with expressions ignore. The getter
        // implements IError.Code, so the IL marks it virtual; a final one cannot be overridden.
        var getter = typeof(Error).GetProperty(nameof(Error.Code))!.GetMethod!;

        // Act
        var overridable = getter.IsVirtual && !getter.IsFinal;

        // Assert
        overridable.ShouldBeFalse();
    }
}
