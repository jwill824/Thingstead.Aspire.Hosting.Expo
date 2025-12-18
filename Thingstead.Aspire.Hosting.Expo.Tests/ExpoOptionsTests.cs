using Aspire.Hosting;
using Xunit;

namespace Thingstead.Aspire.Hosting.Expo.Tests;

public class ExpoOptionsTests
{
    [Fact]
    public void Defaults_AreSet()
    {
        var opts = new ExpoOptions();

        Assert.Equal(string.Empty, opts.ResourceName);
        Assert.Equal(8082, opts.Port);
        Assert.Equal(8082, opts.TargetPort);
        Assert.NotNull(opts.UriCallback);
        Assert.Equal(string.Empty, opts.BuildContext);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var opts = new ExpoOptions
        {
            ResourceName = "my-resource",
            Port = 1111,
            TargetPort = 2222,
            UriCallback = () => "exp://example",
            BuildContext = "/tmp/build"
        };

        Assert.Equal("my-resource", opts.ResourceName);
        Assert.Equal(1111, opts.Port);
        Assert.Equal(2222, opts.TargetPort);
        Assert.Equal("exp://example", opts.UriCallback());
        Assert.Equal("/tmp/build", opts.BuildContext);
    }
}
