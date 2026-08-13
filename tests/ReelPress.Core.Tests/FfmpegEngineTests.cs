using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ReelPress.Core.Tests;

public sealed class FfmpegEngineTests
{
    [Fact]
    public async Task RunFfmpegAsync_ParsesProgress_AndKillsChildProcessOnCancellation()
    {
        var scriptPath = CreateLongRunningProgressScript();

        try
        {
            var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.GetEnvironmentVariable("ComSpec") ?? "C:/Windows/System32/cmd.exe"
                : scriptPath;

            var arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { "/c", scriptPath }
                : Array.Empty<string>();

            var engine = new FfmpegEngine(executable, executable);
            var progressEvents = new List<FfmpegProgress>();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.25));
            var stopwatch = Stopwatch.StartNew();

            var result = await engine.RunFfmpegAsync(
                arguments,
                expectedDuration: TimeSpan.FromSeconds(10),
                progress: new Progress<FfmpegProgress>(item => progressEvents.Add(item)),
                cancellationToken: cts.Token);

            stopwatch.Stop();

            Assert.True(result.WasCanceled);
            Assert.NotEmpty(progressEvents);
            Assert.Contains(progressEvents, evt => evt.Percentage is > 0);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8),
                $"Cancellation should stop the child process quickly. Elapsed={stopwatch.Elapsed}");
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    [Fact]
    public void BinaryResolver_ThrowsClearError_WhenExplicitBinaryIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-ffmpeg-{Guid.NewGuid():N}");
        var resolver = new FfmpegBinaryResolver(new FfmpegEngineOptions
        {
            FfmpegPathOverride = missingPath
        });

        var ex = Assert.Throws<FileNotFoundException>(() => resolver.ResolveFfmpegPath());

        Assert.Contains("Configured ffmpeg path does not exist", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(missingPath, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateLongRunningProgressScript()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var path = Path.Combine(Path.GetTempPath(), $"reelpress-progress-{Guid.NewGuid():N}.cmd");
            var content = "@echo off\r\n:loop\r\necho frame=1 time=00:00:01.00 bitrate=100kbits/s 1>&2\r\nping -n 2 127.0.0.1 >nul\r\ngoto loop\r\n";
            File.WriteAllText(path, content);
            return path;
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"reelpress-progress-{Guid.NewGuid():N}.sh");
        var scriptContent = "#!/usr/bin/env sh\nwhile true; do\n  echo 'frame=1 time=00:00:01.00 bitrate=100kbits/s' 1>&2\n  sleep 0.2\ndone\n";
        File.WriteAllText(scriptPath, scriptContent);

        File.SetUnixFileMode(
            scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        return scriptPath;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }
}
