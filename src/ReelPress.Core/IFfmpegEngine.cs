namespace ReelPress.Core;

public interface IFfmpegEngine
{
    string FfmpegPath { get; }

    string FfprobePath { get; }

    IReadOnlyList<string> BuildSafeArgumentList(params string[] arguments);

    Task<FfmpegRunResult> RunFfmpegAsync(
        IEnumerable<string> arguments,
        TimeSpan? expectedDuration = null,
        IProgress<FfmpegProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ProcessRunResult> RunFfprobeAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default);
}
