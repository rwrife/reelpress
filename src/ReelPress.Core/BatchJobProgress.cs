namespace ReelPress.Core;

public sealed record BatchJobProgress(
    int JobIndex,
    int TotalJobs,
    string InputPath,
    BatchItemStatus Status,
    double? Percent,
    string? Message,
    TimeSpan? ProcessedTime = null);
