using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ReelPress.Core;

public sealed class FfmpegEngine : IFfmpegEngine
{
    private static readonly Regex StderrTimeRegex = new(
        @"time=(\d{2}:\d{2}:\d{2}(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public FfmpegEngine(FfmpegEngineOptions? options = null)
    {
        var resolver = new FfmpegBinaryResolver(options);
        FfmpegPath = resolver.ResolveFfmpegPath();
        FfprobePath = resolver.ResolveFfprobePath();
    }

    public FfmpegEngine(string ffmpegPath, string ffprobePath)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new ArgumentException("ffmpeg path is required.", nameof(ffmpegPath));
        }

        if (string.IsNullOrWhiteSpace(ffprobePath))
        {
            throw new ArgumentException("ffprobe path is required.", nameof(ffprobePath));
        }

        FfmpegPath = ffmpegPath;
        FfprobePath = ffprobePath;
    }

    public string FfmpegPath { get; }

    public string FfprobePath { get; }

    public IReadOnlyList<string> BuildSafeArgumentList(params string[] arguments) =>
        FfmpegArgumentBuilder.Build(arguments);

    public async Task<FfmpegRunResult> RunFfmpegAsync(
        IEnumerable<string> arguments,
        TimeSpan? expectedDuration = null,
        IProgress<FfmpegProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startedAt = DateTimeOffset.UtcNow;
        var safeArguments = FfmpegArgumentBuilder.Build(arguments);
        var progressEvents = new List<FfmpegProgress>();

        var processResult = await RunProcessAsync(
            FfmpegPath,
            safeArguments,
            (line, isStdErr) =>
            {
                if (!TryParseProgress(line, isStdErr, expectedDuration, out var progressEvent)
                    || progressEvent is null)
                {
                    return;
                }

                progressEvents.Add(progressEvent);
                progress?.Report(progressEvent);
            },
            cancellationToken).ConfigureAwait(false);

        return new FfmpegRunResult(
            processResult.ExitCode,
            processResult.StdOut,
            processResult.StdErr,
            processResult.WasCanceled,
            progressEvents.AsReadOnly(),
            startedAt,
            DateTimeOffset.UtcNow);
    }

    public Task<ProcessRunResult> RunFfprobeAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var safeArguments = FfmpegArgumentBuilder.Build(arguments);
        return RunProcessAsync(FfprobePath, safeArguments, onOutputLine: null, cancellationToken);
    }

    private static async Task<ProcessRunResult> RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string, bool>? onOutputLine,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        cancellationToken.ThrowIfCancellationRequested();

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {executablePath}");
        }

        var wasCanceled = false;
        using var registration = cancellationToken.Register(() =>
        {
            wasCanceled = true;
            TryKillProcess(process);
        });

        var stdoutTask = PumpAsync(process.StandardOutput, stdoutBuilder, isStdErr: false, onOutputLine);
        var stderrTask = PumpAsync(process.StandardError, stderrBuilder, isStdErr: true, onOutputLine);

        await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        return new ProcessRunResult(
            process.ExitCode,
            stdoutBuilder.ToString(),
            stderrBuilder.ToString(),
            wasCanceled || cancellationToken.IsCancellationRequested,
            startedAt,
            DateTimeOffset.UtcNow);
    }

    private static async Task PumpAsync(
        StreamReader reader,
        StringBuilder sink,
        bool isStdErr,
        Action<string, bool>? onOutputLine)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            sink.AppendLine(line);
            onOutputLine?.Invoke(line, isStdErr);
        }
    }

    private static bool TryParseProgress(
        string line,
        bool isStdErr,
        TimeSpan? expectedDuration,
        out FfmpegProgress? progress)
    {
        progress = null;

        TimeSpan? processedTime = isStdErr
            ? ParseStderrTime(line)
            : ParseStdoutProgressTime(line);

        if (processedTime is null)
        {
            return false;
        }

        double? percentage = null;
        if (expectedDuration.HasValue && expectedDuration.Value > TimeSpan.Zero)
        {
            percentage = Math.Clamp(
                processedTime.Value.TotalSeconds / expectedDuration.Value.TotalSeconds * 100d,
                0d,
                100d);
        }

        progress = new FfmpegProgress(processedTime.Value, percentage, line);
        return true;
    }

    private static TimeSpan? ParseStdoutProgressTime(string line)
    {
        const string outTimePrefix = "out_time=";
        const string outTimeMsPrefix = "out_time_ms=";
        const string outTimeUsPrefix = "out_time_us=";

        if (line.StartsWith(outTimePrefix, StringComparison.Ordinal))
        {
            return TryParseTimecode(line[outTimePrefix.Length..]);
        }

        if (line.StartsWith(outTimeUsPrefix, StringComparison.Ordinal))
        {
            if (long.TryParse(line[outTimeUsPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros))
            {
                return TimeSpan.FromMilliseconds(micros / 1000d);
            }
        }

        if (line.StartsWith(outTimeMsPrefix, StringComparison.Ordinal))
        {
            if (long.TryParse(line[outTimeMsPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                // ffmpeg's out_time_ms has historically carried microseconds despite the suffix.
                return TimeSpan.FromMilliseconds(value / 1000d);
            }
        }

        return null;
    }

    private static TimeSpan? ParseStderrTime(string line)
    {
        var match = StderrTimeRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        return TryParseTimecode(match.Groups[1].Value);
    }

    private static TimeSpan? TryParseTimecode(string raw)
    {
        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var time))
        {
            return time;
        }

        return null;
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort. Cancellation should never throw from callback.
        }
    }
}
