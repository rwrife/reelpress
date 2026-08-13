namespace ReelPress.Core;

public sealed class FfmpegEngineOptions
{
    public string? BundleRootPath { get; init; }

    public string? FfmpegPathOverride { get; init; }

    public string? FfprobePathOverride { get; init; }
}
