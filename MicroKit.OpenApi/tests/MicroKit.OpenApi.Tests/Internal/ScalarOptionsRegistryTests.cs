using MicroKit.OpenApi.Abstractions;
using MicroKit.OpenApi.Internal;
using MicroKit.OpenApi.Options;
using Xunit;

namespace MicroKit.OpenApi.Tests.Internal;

public sealed class ScalarOptionsRegistryTests
{
    [Fact]
    public void Options_InitializedWithDefaults()
    {
        var registry = new ScalarOptionsRegistry();

        Assert.NotNull(registry.Options);
        Assert.Equal(ScalarTheme.Default, registry.Options.Theme);
        Assert.False(registry.Options.DarkMode);
        Assert.True(registry.Options.ShowSidebar);
        Assert.True(registry.Options.ShowDownloadButton);
    }

    [Fact]
    public void Configure_MutatesOptions()
    {
        var registry = new ScalarOptionsRegistry();

        registry.Configure(o =>
        {
            o.DarkMode = true;
            o.Theme = ScalarTheme.Moon;
        });

        Assert.True(registry.Options.DarkMode);
        Assert.Equal(ScalarTheme.Moon, registry.Options.Theme);
    }

    [Fact]
    public void Configure_CalledMultipleTimes_LastWins()
    {
        var registry = new ScalarOptionsRegistry();

        registry.Configure(o => o.Theme = ScalarTheme.Purple);
        registry.Configure(o => o.Theme = ScalarTheme.Mars);

        Assert.Equal(ScalarTheme.Mars, registry.Options.Theme);
    }

    [Fact]
    public void Configure_WithNullAction_ThrowsArgumentNullException()
    {
        var registry = new ScalarOptionsRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Configure(null!));
    }
}
