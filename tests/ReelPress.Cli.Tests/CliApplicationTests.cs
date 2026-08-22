using System.Text.Json;
using ReelPress.Core;

namespace ReelPress.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task ProbeWithJson_PrintsNormalizedMediaInfo()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var mediaInfo = new MediaInfo(
            "mp4",
            TimeSpan.FromSeconds(12.5),
            1_000_000,
            Array.Empty<MediaStreamInfo>(),
            new VideoStreamInfo("h264", 1920, 1080, 30, 900_000),
            new AudioStreamInfo("aac", 2, 48_000, 100_000));
        var app = new CliApplication(
            new FakeEngine(),
            new FakeProbe(mediaInfo),
            new FakeRecipeStore(),
            output,
            error);

        var exitCode = await app.RunAsync(["probe", "input.mp4", "--json"]);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("mp4", json.RootElement.GetProperty("container").GetString());
        Assert.Equal(12.5, json.RootElement.GetProperty("durationSeconds").GetDouble());
        Assert.Equal(1920, json.RootElement.GetProperty("video").GetProperty("width").GetInt32());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Theory]
    [InlineData("trim", "-ss")]
    [InlineData("convert", "-f")]
    [InlineData("compress", "-crf")]
    [InlineData("resize", "-vf")]
    [InlineData("audio", "-vn")]
    [InlineData("frames", "-frames:v")]
    [InlineData("gif", "-filter_complex")]
    [InlineData("merge", "concat")]
    public async Task StandaloneVerb_RunsThroughSharedPipeline(string verb, string expectedFfmpegArgument)
    {
        var engine = new FakeEngine();
        var output = new StringWriter();
        var error = new StringWriter();
        var app = new CliApplication(engine, new FakeProbe(CreateMediaInfo()), new FakeRecipeStore(), output, error);
        var args = verb switch
        {
            "trim" => new[] { "trim", "input.mp4", "--start", "00:00:01", "--end", "00:00:03", "--out", "clip.mp4" },
            "convert" => new[] { "convert", "input.mp4", "--container", "mkv", "--out", "converted.mkv" },
            "compress" => new[] { "compress", "input.mp4", "--crf", "24", "--out", "small.mp4" },
            "resize" => new[] { "resize", "input.mp4", "--width", "640", "--height", "360", "--out", "resized.mp4" },
            "audio" => new[] { "audio", "input.mp4", "--format", "mp3", "--out", "audio.mp3" },
            "frames" => new[] { "frames", "input.mp4", "--at", "00:00:02", "--out", "frame.png" },
            "gif" => new[] { "gif", "input.mp4", "--start", "00:00:01", "--end", "00:00:03", "--out", "clip.gif" },
            "merge" => new[] { "merge", "input.mp4", "second.mp4", "--out", "merged.mp4" },
            _ => throw new ArgumentOutOfRangeException(nameof(verb))
        };

        var exitCode = await app.RunAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Contains(expectedFfmpegArgument, engine.LastArguments);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RunRecipe_ProcessesEverySupportedFileInFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"reelpress-cli-{Guid.NewGuid():N}");
        var input = Path.Combine(root, "input");
        var outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(input);
        await File.WriteAllTextAsync(Path.Combine(input, "one.mp4"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(input, "two.mov"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(input, "ignore.txt"), "fixture");

        try
        {
            var engine = new FakeEngine();
            var app = new CliApplication(
                engine,
                new FakeProbe(CreateMediaInfo()),
                new FakeRecipeStore(new Recipe("web", [new ConvertOperation(VideoContainer.WebM, VideoCodec.VP9, AudioCodec.Opus)])),
                new StringWriter(),
                new StringWriter());

            var exitCode = await app.RunAsync(["run", "--recipe", "web", input, "--out", outputDirectory]);

            Assert.Equal(0, exitCode);
            Assert.Equal(2, engine.RunCount);
            Assert.Equal(
                [Path.Combine(outputDirectory, "one.webm"), Path.Combine(outputDirectory, "two.webm")],
                engine.AllArguments.Select(arguments => arguments[^1]).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunRecipe_SameStemInputs_UseUniqueDeterministicOutputPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"reelpress-cli-{Guid.NewGuid():N}");
        var input = Path.Combine(root, "input");
        var outputDirectory = Path.Combine(root, "output");
        Directory.CreateDirectory(input);
        await File.WriteAllTextAsync(Path.Combine(input, "clip.mp4"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(input, "clip.mov"), "fixture");

        try
        {
            var engine = new FakeEngine();
            var app = new CliApplication(
                engine,
                new FakeProbe(CreateMediaInfo()),
                new FakeRecipeStore(new Recipe("web", [new ConvertOperation(VideoContainer.WebM, VideoCodec.VP9, AudioCodec.Opus)])),
                new StringWriter(),
                new StringWriter());

            var exitCode = await app.RunAsync(["run", "--recipe", "web", input, "--out", outputDirectory]);

            Assert.Equal(0, exitCode);
            Assert.Equal(
                [Path.Combine(outputDirectory, "clip-mov.webm"), Path.Combine(outputDirectory, "clip-mp4.webm")],
                engine.AllArguments.Select(arguments => arguments[^1]).OrderBy(path => path, StringComparer.Ordinal).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("probe", "input.mp4")]
    [InlineData("run", "input.mp4", "--recipe", "recipe", "--out", "output")]
    [InlineData("trim", "input.mp4", "--end", "00:00:01", "--out", "output.mp4")]
    [InlineData("convert", "input.mp4", "--out", "output.mp4")]
    [InlineData("compress", "input.mp4", "--out", "output.mp4")]
    [InlineData("resize", "input.mp4", "--width", "640", "--out", "output.mp4")]
    [InlineData("audio", "input.mp4", "--out", "output.mp3")]
    [InlineData("frames", "input.mp4", "--at", "1", "--out", "output.png")]
    [InlineData("gif", "input.mp4", "--out", "output.gif")]
    [InlineData("merge", "input.mp4", "second.mp4", "--out", "output.mp4")]
    public async Task Verb_WithUnknownOption_ReturnsUsageError(string verb, params string[] validArguments)
    {
        var error = new StringWriter();
        var app = new CliApplication(
            new FakeEngine(), new FakeProbe(CreateMediaInfo()), new FakeRecipeStore(), new StringWriter(), error);

        var exitCode = await app.RunAsync([verb, .. validArguments, "--otu", "mistyped"]);

        Assert.Equal(CliApplication.UsageErrorExitCode, exitCode);
        Assert.Contains("--otu", error.ToString());
    }

    [Fact]
    public async Task Verb_WithUnsupportedShortOption_ReturnsUsageError()
    {
        var error = new StringWriter();
        var app = new CliApplication(
            new FakeEngine(), new FakeProbe(CreateMediaInfo()), new FakeRecipeStore(), new StringWriter(), error);

        var exitCode = await app.RunAsync(["probe", "input.mp4", "-j"]);

        Assert.Equal(CliApplication.UsageErrorExitCode, exitCode);
        Assert.Contains("-j", error.ToString());
    }

    [Fact]
    public async Task Merge_ProbesEveryInputAndUsesAutoConcatForCompatibleMedia()
    {
        var engine = new FakeEngine();
        var probe = new RecordingProbe(new Dictionary<string, MediaInfo>
        {
            ["one.mp4"] = CreateMediaInfo(),
            ["two.mp4"] = CreateMediaInfo()
        });
        var app = new CliApplication(engine, probe, new FakeRecipeStore(), new StringWriter(), new StringWriter());

        var exitCode = await app.RunAsync(["merge", "one.mp4", "two.mp4", "--out", "merged.mp4"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("one.mp4", probe.ProbedPaths);
        Assert.Contains("two.mp4", probe.ProbedPaths);
        Assert.Contains("concat", engine.LastArguments);
        Assert.Contains("copy", engine.LastArguments);
    }

    [Fact]
    public async Task Merge_ForceConcatDemuxerReceivesMetadataForEveryInput()
    {
        var engine = new FakeEngine();
        var probe = new RecordingProbe(new Dictionary<string, MediaInfo>
        {
            ["one.mp4"] = CreateMediaInfo(),
            ["two.mp4"] = CreateMediaInfo()
        });
        var app = new CliApplication(engine, probe, new FakeRecipeStore(), new StringWriter(), new StringWriter());

        var exitCode = await app.RunAsync([
            "merge", "one.mp4", "two.mp4", "--mode", "forceConcatDemuxer", "--out", "merged.mp4"
        ]);

        Assert.Equal(0, exitCode);
        Assert.Contains("one.mp4", probe.ProbedPaths);
        Assert.Contains("two.mp4", probe.ProbedPaths);
        Assert.Contains("concat", engine.LastArguments);
        Assert.Contains("copy", engine.LastArguments);
    }

    [Fact]
    public async Task Merge_UsesAllProbeResultsToOmitAudioForMixedAudioInputs()
    {
        var withAudio = CreateMediaInfo();
        var withoutAudio = withAudio with { Audio = null };
        var engine = new FakeEngine();
        var probe = new RecordingProbe(new Dictionary<string, MediaInfo>
        {
            ["one.mp4"] = withAudio,
            ["two.mp4"] = withoutAudio
        });
        var app = new CliApplication(engine, probe, new FakeRecipeStore(), new StringWriter(), new StringWriter());

        var exitCode = await app.RunAsync(["merge", "one.mp4", "two.mp4", "--mode", "forceReencode", "--out", "merged.mp4"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("-an", engine.LastArguments);
        Assert.DoesNotContain("[aout]", engine.LastArguments);
    }

    [Theory]
    [InlineData(FrameImageFormat.Png, ".png")]
    [InlineData(FrameImageFormat.Jpeg, ".jpg")]
    public async Task RunRecipe_IntervalFrameExtraction_UsesNumberedImageSequence(FrameImageFormat format, string extension)
    {
        var engine = new FakeEngine();
        var recipe = new Recipe("frames", [new ExtractFramesOperation(everyInterval: TimeSpan.FromSeconds(2), format: format)]);
        var app = new CliApplication(engine, new FakeProbe(CreateMediaInfo()), new FakeRecipeStore(recipe), new StringWriter(), new StringWriter());
        var inputPath = typeof(CliApplicationTests).Assembly.Location;
        var inputName = Path.GetFileNameWithoutExtension(inputPath);

        var exitCode = await app.RunAsync(["run", "--recipe", "frames", inputPath, "--out", Path.GetTempPath()]);

        Assert.Equal(0, exitCode);
        Assert.EndsWith($"{inputName}-%06d{extension}", engine.LastArguments[^1]);
    }

    [Theory]
    [InlineData(FrameImageFormat.Png, ".png")]
    [InlineData(FrameImageFormat.Jpeg, ".jpg")]
    public async Task RunRecipe_SingleFrameExtraction_UsesNormalImageFilename(FrameImageFormat format, string extension)
    {
        var engine = new FakeEngine();
        var recipe = new Recipe("frame", [new ExtractFramesOperation(atTimestamp: TimeSpan.FromSeconds(2), format: format)]);
        var app = new CliApplication(engine, new FakeProbe(CreateMediaInfo()), new FakeRecipeStore(recipe), new StringWriter(), new StringWriter());
        var inputPath = typeof(CliApplicationTests).Assembly.Location;
        var inputName = Path.GetFileNameWithoutExtension(inputPath);

        var exitCode = await app.RunAsync(["run", "--recipe", "frame", inputPath, "--out", Path.GetTempPath()]);

        Assert.Equal(0, exitCode);
        Assert.EndsWith($"{inputName}{extension}", engine.LastArguments[^1]);
        Assert.DoesNotContain("%", engine.LastArguments[^1]);
    }

    [Fact]
    public async Task RunRecipe_UsesLastFormatDeterminingOperationForExtension()
    {
        var engine = new FakeEngine();
        var recipe = new Recipe(
            "web",
            [
                new ConvertOperation(VideoContainer.WebM, VideoCodec.VP9, AudioCodec.Opus),
                new CompressOperation(CompressionMode.QualityCrf, videoCodec: VideoCodec.VP9)
            ]);
        var app = new CliApplication(engine, new FakeProbe(CreateMediaInfo()), new FakeRecipeStore(recipe), new StringWriter(), new StringWriter());
        var inputPath = typeof(CliApplicationTests).Assembly.Location;

        var exitCode = await app.RunAsync(["run", "--recipe", "web", inputPath, "--out", Path.GetTempPath()]);

        Assert.Equal(0, exitCode);
        Assert.EndsWith(".webm", engine.LastArguments[^1]);
    }

    [Fact]
    public async Task ProcessingFailure_ReturnsNonZeroExitCode()
    {
        var app = new CliApplication(
            new FakeEngine(exitCode: 1),
            new FakeProbe(CreateMediaInfo()),
            new FakeRecipeStore(),
            new StringWriter(),
            new StringWriter());

        var exitCode = await app.RunAsync(["compress", "input.mp4", "--out", "output.mp4"]);

        Assert.Equal(CliApplication.ProcessingErrorExitCode, exitCode);
    }

    private static MediaInfo CreateMediaInfo() => new(
        "mp4",
        TimeSpan.FromSeconds(10),
        1_000_000,
        Array.Empty<MediaStreamInfo>(),
        new VideoStreamInfo("h264", 1920, 1080, 30, 900_000),
        new AudioStreamInfo("aac", 2, 48_000, 100_000));

    private sealed class FakeProbe(MediaInfo mediaInfo) : IMediaProbe
    {
        public Task<MediaInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(mediaInfo);

        public MediaInfo ParseProbeJson(string ffprobeJson) => mediaInfo;
    }

    private sealed class RecordingProbe(IReadOnlyDictionary<string, MediaInfo> mediaInfos) : IMediaProbe
    {
        public List<string> ProbedPaths { get; } = [];

        public Task<MediaInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken = default)
        {
            ProbedPaths.Add(inputPath);
            return Task.FromResult(mediaInfos[inputPath]);
        }

        public MediaInfo ParseProbeJson(string ffprobeJson) => throw new NotSupportedException();
    }

    private sealed class FakeEngine(int exitCode = 0) : IFfmpegEngine
    {
        public string FfmpegPath => "ffmpeg";
        public string FfprobePath => "ffprobe";
        public IReadOnlyList<string> LastArguments { get; private set; } = Array.Empty<string>();
        public List<IReadOnlyList<string>> AllArguments { get; } = [];
        public int RunCount { get; private set; }
        public IReadOnlyList<string> BuildSafeArgumentList(params string[] arguments) => arguments;
        public Task<FfmpegRunResult> RunFfmpegAsync(IEnumerable<string> arguments, TimeSpan? expectedDuration = null, IProgress<FfmpegProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            LastArguments = arguments.ToArray();
            AllArguments.Add(LastArguments);
            RunCount++;
            return Task.FromResult(new FfmpegRunResult(
                exitCode,
                string.Empty,
                string.Empty,
                false,
                Array.Empty<FfmpegProgress>(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
        }
        public Task<ProcessRunResult> RunFfprobeAsync(IEnumerable<string> arguments, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRecipeStore(Recipe? recipe = null) : IRecipeStore
    {
        public string RootDirectory => Path.GetTempPath();
        public Task<Recipe> LoadAsync(string nameOrPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(recipe ?? throw new NotSupportedException());
        public Task SaveAsync(string nameOrPath, Recipe recipe, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IReadOnlyList<string> ListPresets() => Array.Empty<string>();
    }
}
