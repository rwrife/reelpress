namespace ReelPress.Core;

public sealed class CompressOperation : IVideoOperation
{
    public CompressOperation(
        CompressionMode mode,
        int crf = 23,
        long targetSizeBytes = 0,
        int audioBitrateKbps = 128,
        VideoCodec videoCodec = VideoCodec.H264)
    {
        Mode = mode;
        Crf = crf;
        TargetSizeBytes = targetSizeBytes;
        AudioBitrateKbps = audioBitrateKbps;
        VideoCodec = videoCodec;
    }

    public string Name => "compress";

    public CompressionMode Mode { get; }

    public int Crf { get; }

    public long TargetSizeBytes { get; }

    public int AudioBitrateKbps { get; }

    public VideoCodec VideoCodec { get; }

    public IReadOnlyList<string> Validate(MediaInfo mediaInfo)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        var errors = new List<string>();

        if (mediaInfo.Video is null)
        {
            errors.Add("Compression requires a video stream.");
        }

        if (AudioBitrateKbps <= 0)
        {
            errors.Add("Audio bitrate must be greater than zero.");
        }

        if (Mode == CompressionMode.QualityCrf)
        {
            if (Crf is < 0 or > 51)
            {
                errors.Add("CRF must be between 0 and 51.");
            }
        }
        else
        {
            if (TargetSizeBytes <= 0)
            {
                errors.Add("Target size must be greater than zero bytes.");
            }

            if (mediaInfo.Duration <= TimeSpan.Zero)
            {
                errors.Add("Target-size compression requires a known positive duration.");
            }
        }

        return errors;
    }

    public void Apply(MediaInfo mediaInfo, VideoOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(context);

        context.AddPostInputArguments("-c:v", CodecMaps.ToVideoEncoder(VideoCodec));
        context.AddPostInputArguments("-preset", "medium");

        if (Mode == CompressionMode.QualityCrf)
        {
            context.AddPostInputArguments("-crf", Crf.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            var bitrate = EstimateVideoBitrateBitsPerSecond(
                targetSizeBytes: TargetSizeBytes,
                duration: mediaInfo.Duration,
                audioBitrateKbps: mediaInfo.Audio is null ? 0 : AudioBitrateKbps,
                hasAudio: mediaInfo.Audio is not null);

            context.AddPostInputArguments("-b:v", FfmpegValueFormatter.FormatKbps(bitrate));
            context.AddPostInputArguments("-maxrate", FfmpegValueFormatter.FormatKbps(bitrate));
            context.AddPostInputArguments("-bufsize", FfmpegValueFormatter.FormatKbps(bitrate * 2));
        }

        if (mediaInfo.Audio is null)
        {
            context.AddPostInputArguments("-an");
        }
        else
        {
            context.AddPostInputArguments("-c:a", "aac", "-b:a", $"{AudioBitrateKbps}k");
        }
    }

    public static long EstimateVideoBitrateBitsPerSecond(
        long targetSizeBytes,
        TimeSpan duration,
        int audioBitrateKbps,
        bool hasAudio)
    {
        if (targetSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSizeBytes), "Target size must be greater than zero.");
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be greater than zero.");
        }

        if (audioBitrateKbps < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(audioBitrateKbps), "Audio bitrate cannot be negative.");
        }

        var targetBitsPerSecond = targetSizeBytes * 8d / duration.TotalSeconds;
        var audioBudgetBitsPerSecond = hasAudio ? audioBitrateKbps * 1000d : 0d;
        var estimated = targetBitsPerSecond - audioBudgetBitsPerSecond;

        return (long)Math.Max(100_000d, estimated);
    }
}
