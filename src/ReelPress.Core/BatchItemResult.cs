namespace ReelPress.Core;

public sealed record BatchItemResult(
    string InputPath,
    string OutputPath,
    BatchItemStatus Status,
    string? Message,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc)
{
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;
    public bool Success => Status == BatchItemStatus.Succeeded;
}
