namespace ReelPress.Core;

public sealed record BatchJob(
    string InputPath,
    string OutputPath,
    IReadOnlyList<IVideoOperation> Operations,
    string? DisplayName = null);
