namespace ReelPress.Core;

public sealed class ResizeOperation : IVideoOperation
{
    public ResizeOperation(
        ResizePreset? preset = null,
        int? width = null,
        int? height = null,
        ResizeMode mode = ResizeMode.Fit,
        bool allowUpscale = false)
    {
        Preset = preset;
        Width = width;
        Height = height;
        Mode = mode;
        AllowUpscale = allowUpscale;
    }

    public string Name => "resize";

    public ResizePreset? Preset { get; }

    public int? Width { get; }

    public int? Height { get; }

    public ResizeMode Mode { get; }

    public bool AllowUpscale { get; }

    public IReadOnlyList<string> Validate(MediaInfo mediaInfo)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        var errors = new List<string>();

        if (mediaInfo.Video is null)
        {
            errors.Add("Resize requires a video stream.");
            return errors;
        }

        if (Preset is null && Width is null && Height is null)
        {
            errors.Add("Specify a preset or custom width/height.");
        }

        if (Width is <= 0)
        {
            errors.Add("Width must be greater than zero when provided.");
        }

        if (Height is <= 0)
        {
            errors.Add("Height must be greater than zero when provided.");
        }

        if (!AllowUpscale && mediaInfo.Video.Width is not null && mediaInfo.Video.Height is not null)
        {
            var target = ResolveTargetDimensions(mediaInfo);
            if (target.width is not null && target.width > mediaInfo.Video.Width)
            {
                errors.Add("Upscale is disabled: requested width is larger than source width.");
            }

            if (target.height is not null && target.height > mediaInfo.Video.Height)
            {
                errors.Add("Upscale is disabled: requested height is larger than source height.");
            }
        }

        return errors;
    }

    public void Apply(MediaInfo mediaInfo, VideoOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(context);

        var filter = BuildScaleFilter(mediaInfo);
        context.AddVideoFilter(filter);

        context.AddPostInputArguments("-c:v", "libx264", "-preset", "medium", "-crf", "18");

        if (mediaInfo.Audio is null)
        {
            context.AddPostInputArguments("-an");
        }
        else
        {
            context.AddPostInputArguments("-c:a", "copy");
        }
    }

    private string BuildScaleFilter(MediaInfo mediaInfo)
    {
        var (targetWidth, targetHeight) = ResolveTargetDimensions(mediaInfo);

        if (targetWidth is null && targetHeight is null)
        {
            throw new InvalidOperationException("Resize operation has no target dimensions.");
        }

        if (targetWidth is not null && targetHeight is not null)
        {
            return Mode switch
            {
                ResizeMode.Pad =>
                    $"scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2",
                ResizeMode.Crop =>
                    $"scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=increase,crop={targetWidth}:{targetHeight}",
                ResizeMode.Stretch =>
                    $"scale={targetWidth}:{targetHeight}",
                _ =>
                    $"scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=decrease"
            };
        }

        if (targetWidth is not null)
        {
            return $"scale={targetWidth}:-2";
        }

        return $"scale=-2:{targetHeight}";
    }

    private (int? width, int? height) ResolveTargetDimensions(MediaInfo mediaInfo)
    {
        if (Width is not null || Height is not null)
        {
            return (Width, Height);
        }

        if (Preset is null)
        {
            return (null, null);
        }

        return Preset.Value switch
        {
            ResizePreset.P1080 => (null, 1080),
            ResizePreset.P720 => (null, 720),
            ResizePreset.P480 => (null, 480),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
