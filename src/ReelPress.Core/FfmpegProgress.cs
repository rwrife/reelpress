namespace ReelPress.Core;

public sealed record FfmpegProgress(
    TimeSpan ProcessedTime,
    double? Percentage,
    string RawLine);
