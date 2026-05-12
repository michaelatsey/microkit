namespace MicroKit.MultiTenancy.Attributes;

/// <summary>Marks a controller or action as exempt from tenant validation.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class SkipTenantValidationAttribute : Attribute
{
}
