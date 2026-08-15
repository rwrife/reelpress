using System.Globalization;

namespace ReelPress.Core;

public sealed class ExportAnimationOperation : IVideoOperation
{
    public ExportAnimationOperation(
        AnimatedImageFormat format,
        TimeSpan start,
        TimeSpan? end = null,
        TimeSpan? duration = null,
        int fps = 15,
        int width = 480)
    {
        Format = format;
        Start = start;
        End = end;
        Duration = duration;
        Fps = fps;
        Width = width;
    }

    public string Name => "export-animation";

    public AnimatedImageFormat Format { get; }

    public TimeSpan Start { get; }

    public TimeSpan? End { get; }

    public TimeSpan? Duration { get; }

    public int Fps { get; }

    public int Width { get; }

    public IReadOnlyList<string> Validate(MediaInfo mediaInfo)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        var errors = new List<string>();

        if (mediaInfo.Video is null)
        {
            errors.Add("Animation export requires a video stream.");
        }

        if (Start < TimeSpan.Zero)
        {
            errors.Add("Start time must be zero or greater.");
        }

        if (End is null && Duration is null)
        {
            errors.Add("Either end or duration must be supplied.");
        }

        if (End is not null && Duration is not null)
        {
            errors.Add("Specify either end or duration, not both.");
        }

        if (End is not null && End <= Start)
        {
            errors.Add("End time must be greater than start time.");
        }

        if (Duration is not null && Duration <= TimeSpan.Zero)
        {
            errors.Add("Duration must be greater than zero.");
        }

        if (Fps <= 0)
        {
            errors.Add("FPS must be greater than zero.");
        }

        if (Width <= 0)
        {
            errors.Add("Width must be greater than zero.");
        }

        var clipDuration = ResolveClipDuration();
        if (clipDuration is not null && mediaInfo.Duration > TimeSpan.Zero)
        {
            if (Start + clipDuration > mediaInfo.Duration)
            {
                errors.Add("Export range exceeds source duration.");
            }
        }

        return errors;
    }

    public void Apply(MediaInfo mediaInfo, VideoOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(context);

        var clipDuration = ResolveClipDuration();

        if (Start > TimeSpan.Zero)
        {
            context.AddPreInputArguments("-ss", FfmpegValueFormatter.FormatTime(Start));
        }

        if (clipDuration is not null)
        {
            context.AddPostInputArguments("-t", FfmpegValueFormatter.FormatTime(clipDuration.Value));
        }

        var fpsToken = Fps.ToString(CultureInfo.InvariantCulture);
        var widthToken = Width.ToString(CultureInfo.InvariantCulture);

        if (Format == AnimatedImageFormat.Gif)
        {
            var gifFilter =
                $"[0:v]fps={fpsToken},scale={widthToken}:-1:flags=lanczos,split[v][p];" +
                "[p]palettegen=stats_mode=full[pal];" +
                "[v][pal]paletteuse=dither=sierra2_4a[vout]";

            context.AddPostInputArguments(
                "-filter_complex", gifFilter,
                "-map", "[vout]",
                "-an",
                "-loop", "0",
                "-f", "gif");

            return;
        }

        var webpFilter =
            $"[0:v]fps={fpsToken},scale={widthToken}:-1:flags=lanczos,split[v][p];" +
            "[p]palettegen=stats_mode=full[pal];" +
            "[v][pal]paletteuse=dither=sierra2_4a,format=rgba[vout]";

        context.AddPostInputArguments(
            "-filter_complex", webpFilter,
            "-map", "[vout]",
            "-an",
            "-c:v", "libwebp_anim",
            "-lossless", "0",
            "-q:v", "70",
            "-loop", "0",
            "-f", "webp");
    }

    private TimeSpan? ResolveClipDuration()
    {
        if (Duration is not null)
        {
            return Duration.Value;
        }

        if (End is not null)
        {
            return End.Value - Start;
        }

        return null;
    }
}
