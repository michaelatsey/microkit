namespace MicroKit.MultiTenancy.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class SkipTenantValidationAttribute : Attribute
{
}
