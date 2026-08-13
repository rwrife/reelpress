namespace ReelPress.Core;

public sealed record ProcessRunResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool WasCanceled,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc)
{
    public bool Success => ExitCode == 0 && !WasCanceled;
}
