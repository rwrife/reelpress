namespace ReelPress.Core.Tests;

public sealed class RecipeRuntimeTests
{
    [Fact]
    public async Task PrepareOperationsAsync_HydratesMergeInputMetadata()
    {
        var media = new MediaInfo(
            "mp4",
            TimeSpan.FromSeconds(5),
            1_000_000,
            Array.Empty<MediaStreamInfo>(),
            new VideoStreamInfo("h264", 1280, 720, 30, 900_000),
            new AudioStreamInfo("aac", 2, 48_000, 100_000));
        var probe = new FakeProbe(media);
        var operations = new IVideoOperation[]
        {
            new MergeOperation(["one.mp4", "two.mp4"], MergeMode.ForceConcatDemuxer)
        };

        var prepared = await RecipeRuntime.PrepareOperationsAsync(operations, probe);
        var errors = VideoOperationPlanner.Validate(media, prepared);

        Assert.Empty(errors);
        Assert.Equal(["one.mp4", "two.mp4"], probe.Paths);
    }

    private sealed class FakeProbe(MediaInfo media) : IMediaProbe
    {
        public List<string> Paths { get; } = [];

        public Task<MediaInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken = default)
        {
            Paths.Add(inputPath);
            return Task.FromResult(media);
        }

        public MediaInfo ParseProbeJson(string ffprobeJson) => media;
    }
}
