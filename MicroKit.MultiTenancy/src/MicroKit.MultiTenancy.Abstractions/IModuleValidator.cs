namespace MicroKit.MultiTenancy.Abstractions;

/// <summary>Validates the configuration of a MicroKit module at startup.</summary>
public interface IModuleValidator
{
    /// <summary>Validates the module configuration and throws if invalid.</summary>
    void Validate();
}
