namespace ReelPress.Core;

public sealed record FfmpegRunResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool WasCanceled,
    IReadOnlyList<FfmpegProgress> ProgressEvents,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc)
{
    public bool Success => ExitCode == 0 && !WasCanceled;
}
