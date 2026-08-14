namespace ReelPress.Core;

/// <summary>
/// Trims a clip to a time range.
/// Stream-copy is fast and lossless but can snap to keyframes;
/// re-encode is slower but frame-accurate.
/// </summary>
public sealed class TrimOperation : IVideoOperation
{
    public TrimOperation(
        TimeSpan start,
        TimeSpan? end = null,
        TimeSpan? duration = null,
        TrimMode mode = TrimMode.AutoPreferCopy)
    {
        Start = start;
        End = end;
        Duration = duration;
        Mode = mode;
    }

    public string Name => "trim";

    public TimeSpan Start { get; }

    public TimeSpan? End { get; }

    public TimeSpan? Duration { get; }

    public TrimMode Mode { get; }

    public IReadOnlyList<string> Validate(MediaInfo mediaInfo)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        var errors = new List<string>();

        if (mediaInfo.Video is null && mediaInfo.Audio is null)
        {
            errors.Add("Media has no audio or video streams to trim.");
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

        var clipDuration = ResolveClipDuration();
        if (clipDuration is not null && mediaInfo.Duration > TimeSpan.Zero)
        {
            if (Start + clipDuration > mediaInfo.Duration)
            {
                errors.Add("Trim range exceeds source duration.");
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

        if (UsesStreamCopy(mediaInfo))
        {
            context.AddPostInputArguments("-c:v", "copy", "-c:a", "copy", "-avoid_negative_ts", "make_zero");
            return;
        }

        context.AddPostInputArguments(
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", "18");

        if (mediaInfo.Audio is null)
        {
            context.AddPostInputArguments("-an");
        }
        else
        {
            context.AddPostInputArguments("-c:a", "aac", "-b:a", "192k");
        }
    }

    public bool UsesStreamCopy(MediaInfo mediaInfo)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        return Mode switch
        {
            TrimMode.ForceCopy => true,
            TrimMode.ForceReencode => false,
            _ => Start == TimeSpan.Zero
        };
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
