namespace ReelPress.Core;

public sealed record PipelineEstimate(
    TimeSpan EstimatedDuration,
    long? EstimatedSizeBytes,
    string Summary);

public static class PipelineEstimator
{
    public static PipelineEstimate Estimate(MediaInfo mediaInfo, IReadOnlyList<IVideoOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(operations);

        var duration = mediaInfo.Duration;
        long? sizeBytes = mediaInfo.Duration > TimeSpan.Zero && mediaInfo.Bitrate is > 0
            ? (long)(mediaInfo.Bitrate.Value * mediaInfo.Duration.TotalSeconds / 8d)
            : null;

        var notes = new List<string>();

        foreach (var operation in operations)
        {
            switch (operation)
            {
                case TrimOperation trim:
                    {
                        var trimmedDuration = ResolveTrimmedDuration(duration, trim);
                        if (trimmedDuration is not null)
                        {
                            if (duration > TimeSpan.Zero && sizeBytes is not null)
                            {
                                var ratio = trimmedDuration.Value.TotalSeconds / duration.TotalSeconds;
                                sizeBytes = (long)(sizeBytes.Value * Math.Clamp(ratio, 0d, 1d));
                            }

                            duration = trimmedDuration.Value;
                            notes.Add($"Trim to {duration:c}");
                        }

                        break;
                    }

                case ResizeOperation resize:
                    {
                        if (sizeBytes is not null && mediaInfo.Video?.Width is > 0 && mediaInfo.Video?.Height is > 0)
                        {
                            var (w, h) = ResolveResizeTarget(mediaInfo.Video.Width.Value, mediaInfo.Video.Height.Value, resize);
                            if (w > 0 && h > 0)
                            {
                                var sourcePixels = (double)mediaInfo.Video.Width.Value * mediaInfo.Video.Height.Value;
                                var targetPixels = (double)w * h;
                                var ratio = targetPixels / sourcePixels;
                                sizeBytes = (long)(sizeBytes.Value * Math.Clamp(ratio, 0.15d, 1.75d));
                                notes.Add($"Resize to {w}x{h}");
                            }
                        }

                        break;
                    }

                case CompressOperation compress:
                    {
                        if (compress.Mode == CompressionMode.TargetSize && compress.TargetSizeBytes > 0)
                        {
                            sizeBytes = compress.TargetSizeBytes;
                            notes.Add($"Target size ~{FormatSize(sizeBytes)}");
                        }
                        else if (sizeBytes is not null)
                        {
                            var factor = compress.Crf switch
                            {
                                <= 18 => 1.0,
                                <= 23 => 0.72,
                                <= 28 => 0.48,
                                <= 34 => 0.32,
                                _ => 0.22
                            };

                            sizeBytes = (long)(sizeBytes.Value * factor);
                            notes.Add($"CRF {compress.Crf}");
                        }

                        break;
                    }

                case MuteOperation:
                    if (sizeBytes is not null && mediaInfo.Audio is not null)
                    {
                        sizeBytes = (long)(sizeBytes.Value * 0.9d);
                        notes.Add("Remove audio");
                    }

                    break;

                case ExtractAudioOperation extractAudio:
                    {
                        if (duration > TimeSpan.Zero)
                        {
                            var audioBitrate = extractAudio.Format is AudioExtractionFormat.Mp3 or AudioExtractionFormat.Aac
                                ? extractAudio.BitrateKbps * 1000L
                                : 1_411_200L;

                            sizeBytes = (long)(audioBitrate * duration.TotalSeconds / 8d);
                            notes.Add("Audio-only output");
                        }

                        break;
                    }

                case ExportAnimationOperation animation:
                    {
                        var clipDuration = ResolveAnimationDuration(duration, animation);
                        if (clipDuration is not null)
                        {
                            duration = clipDuration.Value;
                        }

                        if (sizeBytes is not null)
                        {
                            sizeBytes = (long)(sizeBytes.Value * 0.25d);
                        }

                        notes.Add(animation.Format == AnimatedImageFormat.Gif
                            ? "GIF export"
                            : "Animated WebP export");

                        break;
                    }
            }
        }

        var summary = sizeBytes is null
            ? "Estimate unavailable (missing source bitrate metadata)."
            : $"Estimated output: {FormatSize(sizeBytes)} · {duration:c}";

        if (notes.Count > 0)
        {
            summary = $"{summary} ({string.Join(", ", notes)})";
        }

        return new PipelineEstimate(duration, sizeBytes, summary);
    }

    private static TimeSpan? ResolveTrimmedDuration(TimeSpan sourceDuration, TrimOperation trim)
    {
        if (trim.Duration is not null)
        {
            return trim.Duration.Value;
        }

        if (trim.End is not null)
        {
            return trim.End.Value - trim.Start;
        }

        if (sourceDuration > TimeSpan.Zero)
        {
            return sourceDuration - trim.Start;
        }

        return null;
    }

    private static TimeSpan? ResolveAnimationDuration(TimeSpan sourceDuration, ExportAnimationOperation animation)
    {
        if (animation.Duration is not null)
        {
            return animation.Duration;
        }

        if (animation.End is not null)
        {
            return animation.End.Value - animation.Start;
        }

        if (sourceDuration > TimeSpan.Zero)
        {
            return sourceDuration - animation.Start;
        }

        return null;
    }

    private static (int width, int height) ResolveResizeTarget(int sourceWidth, int sourceHeight, ResizeOperation resize)
    {
        var width = resize.Width;
        var height = resize.Height;

        if (width is null && height is null && resize.Preset is not null)
        {
            height = resize.Preset switch
            {
                ResizePreset.P1080 => 1080,
                ResizePreset.P720 => 720,
                ResizePreset.P480 => 480,
                _ => height
            };
        }

        if (width is null && height is null)
        {
            return (sourceWidth, sourceHeight);
        }

        if (width is null && height is not null)
        {
            var ratio = (double)height.Value / sourceHeight;
            width = (int)Math.Round(sourceWidth * ratio);
        }

        if (height is null && width is not null)
        {
            var ratio = (double)width.Value / sourceWidth;
            height = (int)Math.Round(sourceHeight * ratio);
        }

        return (Math.Max(1, width ?? sourceWidth), Math.Max(1, height ?? sourceHeight));
    }

    private static string FormatSize(long? bytes)
    {
        if (bytes is null || bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes.Value;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
