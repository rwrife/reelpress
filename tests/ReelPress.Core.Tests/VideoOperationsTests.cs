namespace ReelPress.Core.Tests;

public sealed class VideoOperationsTests
{
    [Fact]
    public void TrimOperation_AutoMode_UsesStreamCopy_WhenTrimStartsAtZero()
    {
        var media = CreateSampleMediaInfo();
        var operation = new TrimOperation(
            start: TimeSpan.Zero,
            duration: TimeSpan.FromSeconds(8),
            mode: TrimMode.AutoPreferCopy);

        var args = BuildArgs(media, operation);

        AssertContainsSequence(args, "-c:v", "copy");
        AssertContainsSequence(args, "-c:a", "copy");
        Assert.DoesNotContain("libx264", args);
    }

    [Fact]
    public void TrimOperation_AutoMode_Reencodes_WhenTrimStartsMidStream()
    {
        var media = CreateSampleMediaInfo();
        var operation = new TrimOperation(
            start: TimeSpan.FromSeconds(5),
            end: TimeSpan.FromSeconds(15),
            mode: TrimMode.AutoPreferCopy);

        var args = BuildArgs(media, operation);

        AssertContainsSequence(args, "-c:v", "libx264");
        AssertContainsSequence(args, "-c:a", "aac");
        Assert.False(ContainsSequence(args, "-c:v", "copy"));
    }

    [Fact]
    public void ConvertOperation_ValidatesIncompatibleWebmAudioCodec()
    {
        var media = CreateSampleMediaInfo();
        var operation = new ConvertOperation(
            container: VideoContainer.WebM,
            videoCodec: VideoCodec.VP9,
            audioCodec: AudioCodec.Mp3);

        var errors = operation.Validate(media);

        Assert.Contains(errors, error => error.Contains("WebM requires Opus audio", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConvertOperation_BuildsExpectedCodecArgs_ForMp4H265Aac()
    {
        var media = CreateSampleMediaInfo();
        var operation = new ConvertOperation(
            container: VideoContainer.Mp4,
            videoCodec: VideoCodec.H265,
            audioCodec: AudioCodec.Aac);

        var args = BuildArgs(media, operation);

        AssertContainsSequence(args, "-f", "mp4");
        AssertContainsSequence(args, "-c:v", "libx265");
        AssertContainsSequence(args, "-c:a", "aac");
        AssertContainsSequence(args, "-movflags", "+faststart");
    }

    [Fact]
    public void CompressOperation_TargetSize_EstimatesBitrate_WithAudioBudgetSubtracted()
    {
        var bitrate = CompressOperation.EstimateVideoBitrateBitsPerSecond(
            targetSizeBytes: 10_000_000,
            duration: TimeSpan.FromSeconds(100),
            audioBitrateKbps: 128,
            hasAudio: true);

        var expected = (long)(10_000_000 * 8d / 100d - 128_000d);

        Assert.Equal(expected, bitrate);
    }

    [Fact]
    public void CompressOperation_TargetSize_BuildsBitrateArguments()
    {
        var media = CreateSampleMediaInfo(duration: TimeSpan.FromSeconds(30));
        var operation = new CompressOperation(
            mode: CompressionMode.TargetSize,
            targetSizeBytes: 2_000_000,
            audioBitrateKbps: 96,
            videoCodec: VideoCodec.H264);

        var args = BuildArgs(media, operation);

        AssertContainsSequence(args, "-c:v", "libx264");
        Assert.Contains("-b:v", args);
        Assert.Contains("-maxrate", args);
        Assert.Contains("-bufsize", args);
        AssertContainsSequence(args, "-c:a", "aac");
        AssertContainsSequence(args, "-b:a", "96k");
    }

    [Fact]
    public void ResizeOperation_Preset720_UsesAspectSafeScale()
    {
        var media = CreateSampleMediaInfo();
        var operation = new ResizeOperation(preset: ResizePreset.P720);

        var args = BuildArgs(media, operation);

        AssertContainsSequence(args, "-vf", "scale=-2:720");
    }

    [Fact]
    public void ResizeOperation_CustomPad_BuildsPadFilter()
    {
        var media = CreateSampleMediaInfo();
        var operation = new ResizeOperation(width: 640, height: 640, mode: ResizeMode.Pad);

        var args = BuildArgs(media, operation);

        AssertContainsSequence(
            args,
            "-vf",
            "scale=640:640:force_original_aspect_ratio=decrease,pad=640:640:(ow-iw)/2:(oh-ih)/2");
    }

    [Fact]
    public void ResizeOperation_Stretch_IsOnlyModeThatCanDistortAspectRatio()
    {
        var media = CreateSampleMediaInfo();
        var operation = new ResizeOperation(width: 640, height: 480, mode: ResizeMode.Stretch);

        var args = BuildArgs(media, operation);

        AssertContainsSequence(args, "-vf", "scale=640:480");
        Assert.DoesNotContain(args, argument => argument.Contains("force_original_aspect_ratio", StringComparison.Ordinal));
    }

    [Fact]
    public void ResizeOperation_ValidationFails_WhenUpscaleDisabledAndTargetExceedsSource()
    {
        var media = CreateSampleMediaInfo(width: 640, height: 360);
        var operation = new ResizeOperation(width: 1920, height: 1080, allowUpscale: false);

        var errors = operation.Validate(media);

        Assert.Contains(errors, error => error.Contains("Upscale is disabled", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> BuildArgs(MediaInfo mediaInfo, IVideoOperation operation)
    {
        var errors = operation.Validate(mediaInfo);
        Assert.Empty(errors);

        var context = new VideoOperationContext();
        operation.Apply(mediaInfo, context);
        return context.BuildArguments("input.mp4", "output.mp4");
    }

    private static MediaInfo CreateSampleMediaInfo(
        TimeSpan? duration = null,
        int width = 1920,
        int height = 1080)
    {
        var streams = new List<MediaStreamInfo>
        {
            new(
                Index: 0,
                CodecType: "video",
                CodecName: "h264",
                Bitrate: 2_000_000,
                Width: width,
                Height: height,
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
            Video: new VideoStreamInfo("h264", width, height, 30, 2_000_000),
            Audio: new AudioStreamInfo("aac", 2, 48_000, 128_000));
    }

    private static void AssertContainsSequence(IReadOnlyList<string> args, string first, string second)
    {
        var index = FindIndex(args, first);
        Assert.True(index >= 0, $"Did not find token '{first}' in args: {string.Join(' ', args)}");
        Assert.True(index + 1 < args.Count, $"Token '{first}' did not have a following value.");
        Assert.Equal(second, args[index + 1]);
    }

    private static bool ContainsSequence(IReadOnlyList<string> args, string first, string second)
    {
        var index = FindIndex(args, first);
        return index >= 0 && index + 1 < args.Count && string.Equals(args[index + 1], second, StringComparison.Ordinal);
    }

    private static int FindIndex(IReadOnlyList<string> args, string token)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], token, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
