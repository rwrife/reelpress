using System.Globalization;

namespace ReelPress.Core;

public sealed class ExtractFramesOperation : IVideoOperation
{
    public ExtractFramesOperation(
        TimeSpan? everyInterval = null,
        TimeSpan? atTimestamp = null,
        FrameImageFormat format = FrameImageFormat.Png,
        int jpegQuality = 2)
    {
        EveryInterval = everyInterval;
        AtTimestamp = atTimestamp;
        Format = format;
        JpegQuality = jpegQuality;
    }

    public string Name => "extract-frames";

    public TimeSpan? EveryInterval { get; }

    public TimeSpan? AtTimestamp { get; }

    public FrameImageFormat Format { get; }

    public int JpegQuality { get; }

    public IReadOnlyList<string> Validate(MediaInfo mediaInfo)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        var errors = new List<string>();

        if (mediaInfo.Video is null)
        {
            errors.Add("Frame extraction requires a video stream.");
            return errors;
        }

        if ((EveryInterval is null && AtTimestamp is null) ||
            (EveryInterval is not null && AtTimestamp is not null))
        {
            errors.Add("Specify either extraction interval or single timestamp mode.");
        }

        if (EveryInterval is not null && EveryInterval <= TimeSpan.Zero)
        {
            errors.Add("Extraction interval must be greater than zero.");
        }

        if (AtTimestamp is not null && AtTimestamp < TimeSpan.Zero)
        {
            errors.Add("Frame timestamp must be zero or greater.");
        }

        if (mediaInfo.Duration > TimeSpan.Zero && AtTimestamp is not null && AtTimestamp > mediaInfo.Duration)
        {
            errors.Add("Frame timestamp exceeds source duration.");
        }

        if (JpegQuality is < 2 or > 31)
        {
            errors.Add("JPEG quality must be between 2 (best) and 31 (worst).");
        }

        return errors;
    }

    public void Apply(MediaInfo mediaInfo, VideoOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(context);

        if (AtTimestamp is not null && AtTimestamp > TimeSpan.Zero)
        {
            context.AddPreInputArguments("-ss", FfmpegValueFormatter.FormatTime(AtTimestamp.Value));
        }

        if (EveryInterval is not null)
        {
            var fps = 1d / EveryInterval.Value.TotalSeconds;
            context.AddVideoFilter($"fps={fps.ToString("0.######", CultureInfo.InvariantCulture)}");
            context.AddPostInputArguments("-vsync", "vfr");
        }

        context.AddPostInputArguments("-map", "0:v:0", "-an");

        if (AtTimestamp is not null)
        {
            context.AddPostInputArguments("-frames:v", "1");
        }

        switch (Format)
        {
            case FrameImageFormat.Png:
                context.AddPostInputArguments("-c:v", "png");
                break;

            case FrameImageFormat.Jpeg:
                context.AddPostInputArguments("-c:v", "mjpeg", "-q:v", JpegQuality.ToString(CultureInfo.InvariantCulture));
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
