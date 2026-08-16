namespace ReelPress.Core.Tests;

public sealed class PipelineEstimatorTests
{
    [Fact]
    public void Estimate_IncludesTargetSize_WhenCompressionModeIsTargetSize()
    {
        var media = CreateSampleMediaInfo();
        var operations = new IVideoOperation[]
        {
            new CompressOperation(
                mode: CompressionMode.TargetSize,
                targetSizeBytes: 10 * 1024 * 1024,
                audioBitrateKbps: 128,
                videoCodec: VideoCodec.H264)
        };

        var estimate = PipelineEstimator.Estimate(media, operations);

        Assert.Equal(10 * 1024 * 1024, estimate.EstimatedSizeBytes);
        Assert.Contains("Estimated output", estimate.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Estimate_AdjustsDuration_WhenTrimOperationExists()
    {
        var media = CreateSampleMediaInfo(duration: TimeSpan.FromMinutes(2));
        var operations = new IVideoOperation[]
        {
            new TrimOperation(start: TimeSpan.FromSeconds(10), end: TimeSpan.FromSeconds(40))
        };

        var estimate = PipelineEstimator.Estimate(media, operations);

        Assert.Equal(TimeSpan.FromSeconds(30), estimate.EstimatedDuration);
        Assert.Contains("Trim", estimate.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static MediaInfo CreateSampleMediaInfo(TimeSpan? duration = null)
    {
        var streams = new List<MediaStreamInfo>
        {
            new(
                Index: 0,
                CodecType: "video",
                CodecName: "h264",
                Bitrate: 2_000_000,
                Width: 1920,
                Height: 1080,
                Fps: 30,
                Channels: null,
                SampleRate: null),
            new(
                Index: 1,
                CodecType: "audio",
                CodecName: "aac",
                Bitrate: 128_000,
                Width: null,
                Height: null,
                Fps: null,
                Channels: 2,
                SampleRate: 48_000)
        };

        return new MediaInfo(
            Container: "mp4",
            Duration: duration ?? TimeSpan.FromSeconds(120),
            Bitrate: 2_128_000,
            Streams: streams,
            Video: new VideoStreamInfo("h264", 1920, 1080, 30, 2_000_000),
            Audio: new AudioStreamInfo("aac", 2, 48_000, 128_000));
    }
}
