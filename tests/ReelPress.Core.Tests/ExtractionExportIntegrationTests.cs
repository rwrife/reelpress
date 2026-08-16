using System.Diagnostics;

namespace ReelPress.Core.Tests;

public sealed class ExtractionExportIntegrationTests
{
    [Fact]
    public async Task ExtractAudio_ProducesMp3_WhenFfmpegAvailable()
    {
        if (!CanRunFfmpeg())
        {
            return;
        }

        var tempRoot = CreateTempRoot("audio");
        var inputPath = Path.Combine(tempRoot, "input.mp4");
        var outputPath = Path.Combine(tempRoot, "audio.mp3");

        try
        {
            await GenerateSampleClipAsync(inputPath, width: 640, height: 360, durationSeconds: 4);

            var engine = new FfmpegEngine("ffmpeg", "ffprobe");
            var probe = new MediaProbe(engine);
            var media = await probe.ProbeAsync(inputPath);

            var operation = new ExtractAudioOperation(AudioExtractionFormat.Mp3, bitrateKbps: 128);
            var args = VideoOperationPlanner.BuildArguments(media, inputPath, outputPath, operation);
            var result = await engine.RunFfmpegAsync(args, expectedDuration: media.Duration);

            Assert.True(result.Success, result.StdErr);
            Assert.True(File.Exists(outputPath));

            var extracted = await probe.ProbeAsync(outputPath);
            Assert.NotNull(extracted.Audio);
            Assert.True(string.Equals(extracted.Audio!.Codec, "mp3", StringComparison.OrdinalIgnoreCase));
            Assert.Null(extracted.Video);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ExtractFrames_IntervalAndSingleTimestamp_WorkAsExpected_WhenFfmpegAvailable()
    {
        if (!CanRunFfmpeg())
        {
            return;
        }

        var tempRoot = CreateTempRoot("frames");
        var inputPath = Path.Combine(tempRoot, "input.mp4");
        var intervalPattern = Path.Combine(tempRoot, "frames", "frame-%03d.png");
        var singleFramePath = Path.Combine(tempRoot, "single.jpg");

        Directory.CreateDirectory(Path.GetDirectoryName(intervalPattern)!);

        try
        {
            await GenerateSampleClipAsync(inputPath, width: 640, height: 360, durationSeconds: 3);

            var engine = new FfmpegEngine("ffmpeg", "ffprobe");
            var probe = new MediaProbe(engine);
            var media = await probe.ProbeAsync(inputPath);

            var intervalOperation = new ExtractFramesOperation(
                everyInterval: TimeSpan.FromSeconds(1),
                format: FrameImageFormat.Png);

            var intervalArgs = VideoOperationPlanner.BuildArguments(media, inputPath, intervalPattern, intervalOperation);
            var intervalResult = await engine.RunFfmpegAsync(intervalArgs, expectedDuration: media.Duration);

            Assert.True(intervalResult.Success, intervalResult.StdErr);

            var intervalFrames = Directory.GetFiles(Path.Combine(tempRoot, "frames"), "frame-*.png");
            Assert.Equal(3, intervalFrames.Length);

            var singleFrameOperation = new ExtractFramesOperation(
                atTimestamp: TimeSpan.FromSeconds(1.5),
                format: FrameImageFormat.Jpeg);

            var singleArgs = VideoOperationPlanner.BuildArguments(media, inputPath, singleFramePath, singleFrameOperation);
            var singleResult = await engine.RunFfmpegAsync(singleArgs, expectedDuration: media.Duration);

            Assert.True(singleResult.Success, singleResult.StdErr);
            Assert.True(File.Exists(singleFramePath));

            var jpgCount = Directory.GetFiles(tempRoot, "*.jpg").Length;
            Assert.Equal(1, jpgCount);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ExportGif_UsesPalettePipeline_AndProducesAnimatedGif_WhenFfmpegAvailable()
    {
        if (!CanRunFfmpeg())
        {
            return;
        }

        var tempRoot = CreateTempRoot("gif");
        var inputPath = Path.Combine(tempRoot, "input.mp4");
        var outputPath = Path.Combine(tempRoot, "clip.gif");

        try
        {
            await GenerateSampleClipAsync(inputPath, width: 640, height: 360, durationSeconds: 4);

            var engine = new FfmpegEngine("ffmpeg", "ffprobe");
            var probe = new MediaProbe(engine);
            var media = await probe.ProbeAsync(inputPath);

            var operation = new ExportAnimationOperation(
                format: AnimatedImageFormat.Gif,
                start: TimeSpan.FromSeconds(0.5),
                end: TimeSpan.FromSeconds(2.5),
                fps: 12,
                width: 320);

            var args = VideoOperationPlanner.BuildArguments(media, inputPath, outputPath, operation);
            var result = await engine.RunFfmpegAsync(args, expectedDuration: TimeSpan.FromSeconds(2));

            Assert.True(result.Success, result.StdErr);
            Assert.True(File.Exists(outputPath));
            Assert.Contains("palettegen", string.Join(' ', args), StringComparison.Ordinal);
            Assert.Contains("paletteuse", string.Join(' ', args), StringComparison.Ordinal);

            var gifMedia = await probe.ProbeAsync(outputPath);
            Assert.NotNull(gifMedia.Video);
            Assert.True(gifMedia.Duration > TimeSpan.Zero);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task Merge_SupportsCompatibleAndMixedInputs_WhenFfmpegAvailable()
    {
        if (!CanRunFfmpeg())
        {
            return;
        }

        var tempRoot = CreateTempRoot("merge");
        var clipA = Path.Combine(tempRoot, "clip-a.mp4");
        var clipB = Path.Combine(tempRoot, "clip-b.mp4");
        var clipC = Path.Combine(tempRoot, "clip-c.mp4");
        var mergedFast = Path.Combine(tempRoot, "merged-fast.mp4");
        var mergedMixed = Path.Combine(tempRoot, "merged-mixed.mp4");

        try
        {
            await GenerateSampleClipAsync(clipA, width: 640, height: 360, durationSeconds: 2);
            await GenerateSampleClipAsync(clipB, width: 640, height: 360, durationSeconds: 2);
            await GenerateSampleClipAsync(clipC, width: 960, height: 540, durationSeconds: 2);

            var engine = new FfmpegEngine("ffmpeg", "ffprobe");
            var probe = new MediaProbe(engine);

            var mediaA = await probe.ProbeAsync(clipA);
            var mediaB = await probe.ProbeAsync(clipB);
            var mediaC = await probe.ProbeAsync(clipC);

            var compatibleMerge = new MergeOperation(
                inputPaths: new[] { clipA, clipB },
                mode: MergeMode.Auto,
                inputMediaInfos: new[] { mediaA, mediaB });

            var fastArgs = VideoOperationPlanner.BuildArguments(mediaA, clipA, mergedFast, compatibleMerge);
            var fastResult = await engine.RunFfmpegAsync(fastArgs, expectedDuration: TimeSpan.FromSeconds(4));

            Assert.True(fastResult.Success, fastResult.StdErr);
            Assert.Contains("-f", fastArgs);
            Assert.Contains("concat", fastArgs);

            var mixedMerge = new MergeOperation(
                inputPaths: new[] { clipA, clipC },
                mode: MergeMode.Auto,
                inputMediaInfos: new[] { mediaA, mediaC });

            var mixedArgs = VideoOperationPlanner.BuildArguments(mediaA, clipA, mergedMixed, mixedMerge);
            var mixedResult = await engine.RunFfmpegAsync(mixedArgs, expectedDuration: TimeSpan.FromSeconds(4));

            Assert.True(mixedResult.Success, mixedResult.StdErr);
            Assert.Contains(mixedArgs, token => token.Contains("concat=n=2", StringComparison.Ordinal));

            var mixedMedia = await probe.ProbeAsync(mergedMixed);
            Assert.NotNull(mixedMedia.Video);
            Assert.True(mixedMedia.Duration >= TimeSpan.FromSeconds(3.5));
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

    private static async Task GenerateSampleClipAsync(string outputPath, int width, int height, int durationSeconds)
    {
        var engine = new FfmpegEngine("ffmpeg", "ffprobe");
        var args = FfmpegArgumentBuilder.Build(
            "-y",
            "-f", "lavfi",
            "-i", $"testsrc2=size={width}x{height}:rate=30",
            "-f", "lavfi",
            "-i", "sine=frequency=440:sample_rate=48000",
            "-t", durationSeconds.ToString(),
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", "128k",
            outputPath);

        var result = await engine.RunFfmpegAsync(args, expectedDuration: TimeSpan.FromSeconds(durationSeconds));
        Assert.True(result.Success, result.StdErr);
    }

    private static string CreateTempRoot(string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"reelpress-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
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
