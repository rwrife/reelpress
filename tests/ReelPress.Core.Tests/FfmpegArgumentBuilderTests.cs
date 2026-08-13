namespace ReelPress.Core.Tests;

public sealed class FfmpegArgumentBuilderTests
{
    [Fact]
    public void Build_PreservesPotentiallyDangerousTokens_AsSingleArguments()
    {
        var dangerous = "input.mp4; rm -rf / $(touch pwned)";

        var args = FfmpegArgumentBuilder.Build("-i", dangerous, "-c:v", "libx264", "output.mp4");

        Assert.Equal(5, args.Count);
        Assert.Equal("-i", args[0]);
        Assert.Equal(dangerous, args[1]);
        Assert.Equal("-c:v", args[2]);
        Assert.Equal("libx264", args[3]);
        Assert.Equal("output.mp4", args[4]);
    }
}
