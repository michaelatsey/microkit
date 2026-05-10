namespace MicroKit.OpenApi.Internal;

/// <summary>
/// Internal registry for Scalar UI options.
/// </summary>
internal sealed class ScalarOptionsRegistry
{
    public MicroKit.OpenApi.Abstractions.ScalarOptions Options { get; } = new();

    public void Configure(Action<MicroKit.OpenApi.Abstractions.ScalarOptions> configure)
    {
        configure(Options);
    }
}
