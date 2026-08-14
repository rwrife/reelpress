using System.Diagnostics;

namespace ReelPress.Core.Tests;

public sealed class CompressOperationIntegrationTests
{
    [Fact]
    public async Task TargetSizeCompression_LandsWithinReasonableTolerance_WhenFfmpegAvailable()
    {
        if (!CanRunFfmpeg())
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"reelpress-compress-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var inputPath = Path.Combine(tempRoot, "input.mp4");
        var outputPath = Path.Combine(tempRoot, "output.mp4");

        try
        {
            await GenerateSampleClipAsync(inputPath);

            var engine = new FfmpegEngine("ffmpeg", "ffprobe");
            var probe = new MediaProbe(engine);
            var media = await probe.ProbeAsync(inputPath);

            const long targetSizeBytes = 900_000;
            var operation = new CompressOperation(
                mode: CompressionMode.TargetSize,
                targetSizeBytes: targetSizeBytes,
                audioBitrateKbps: 96,
                videoCodec: VideoCodec.H264);

            var args = VideoOperationPlanner.BuildArguments(media, inputPath, outputPath, operation);
            var result = await engine.RunFfmpegAsync(args, expectedDuration: media.Duration);

            Assert.True(result.Success, result.StdErr);

            var producedSize = new FileInfo(outputPath).Length;
            var ratio = producedSize / (double)targetSizeBytes;

            Assert.InRange(ratio, 0.60d, 1.40d);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static bool CanRunFfmpeg()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-version");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task GenerateSampleClipAsync(string outputPath)
    {
        var engine = new FfmpegEngine("ffmpeg", "ffprobe");
        var args = FfmpegArgumentBuilder.Build(
            "-y",
            "-f", "lavfi",
            "-i", "testsrc2=size=1280x720:rate=30",
            "-f", "lavfi",
            "-i", "sine=frequency=1000:sample_rate=48000",
            "-t", "6",
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", "128k",
            outputPath);

        var result = await engine.RunFfmpegAsync(args, expectedDuration: TimeSpan.FromSeconds(6));
        Assert.True(result.Success, result.StdErr);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }
}
