namespace ReelPress.Core.Tests;

public sealed class PipelineRunnerTests
{
    [Fact]
    public async Task RunAsync_Succeeds_ForValidJob()
    {
        var engine = new FakeFfmpegEngine(exitCode: 0);
        var probe = new FakeMediaProbe(CreateSampleMediaInfo());
        var runner = new PipelineRunner(engine, probe);

        var outputPath = Path.Combine(Path.GetTempPath(), $"reelpress-test-{Guid.NewGuid():N}.mp4");
        var job = new BatchJob(
            InputPath: "input.mp4",
            OutputPath: outputPath,
            Operations: new IVideoOperation[]
            {
                new ResizeOperation(preset: ResizePreset.P720),
                new CompressOperation(mode: CompressionMode.QualityCrf, crf: 24)
            });

        var results = await runner.RunAsync(new[] { job });

        var result = Assert.Single(results);
        Assert.Equal(BatchItemStatus.Succeeded, result.Status);
        Assert.Equal(job.InputPath, result.InputPath);
        Assert.Equal(job.OutputPath, result.OutputPath);
        Assert.NotEmpty(engine.LastFfmpegArgs);
    }

    [Fact]
    public async Task RunAsync_Skips_WhenOperationValidationFails()
    {
        var engine = new FakeFfmpegEngine(exitCode: 0);
        var probe = new FakeMediaProbe(CreateSampleMediaInfo());
        var runner = new PipelineRunner(engine, probe);

        var job = new BatchJob(
            InputPath: "input.mp4",
            OutputPath: "output.mp4",
            Operations: new IVideoOperation[]
            {
                new ResizeOperation(width: 0, height: 720)
            });

        var results = await runner.RunAsync(new[] { job });

        var result = Assert.Single(results);
        Assert.Equal(BatchItemStatus.Skipped, result.Status);
        Assert.Contains("greater than zero", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(engine.LastFfmpegArgs);
    }

    [Fact]
    public async Task RunAsync_Fails_WhenFfmpegReturnsError()
    {
        var engine = new FakeFfmpegEngine(exitCode: 1, stderr: "encoding failed");
        var probe = new FakeMediaProbe(CreateSampleMediaInfo());
        var runner = new PipelineRunner(engine, probe);

        var job = new BatchJob(
            InputPath: "input.mp4",
            OutputPath: "output.mp4",
            Operations: new IVideoOperation[]
            {
                new CompressOperation(mode: CompressionMode.QualityCrf, crf: 28)
            });

        var results = await runner.RunAsync(new[] { job });

        var result = Assert.Single(results);
        Assert.Equal(BatchItemStatus.Failed, result.Status);
        Assert.Contains("encoding failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MediaInfo CreateSampleMediaInfo()
    {
        var streams = new List<MediaStreamInfo>
        {
            new(
                Index: 0,
                CodecType: "video",
                CodecName: "h264",
                Bitrate: 2_000_000,
                Width: 1920,
                Height: 1080,
                Fps: 30,
                Channels: null,
                SampleRate: null),
            new(
                Index: 1,
                CodecType: "audio",
                CodecName: "aac",
                Bitrate: 128_000,
                Width: null,
                Height: null,
                Fps: null,
                Channels: 2,
                SampleRate: 48_000)
        };

        return new MediaInfo(
            Container: "mp4",
            Duration: TimeSpan.FromSeconds(60),
            Bitrate: 2_128_000,
            Streams: streams,
            Video: new VideoStreamInfo("h264", 1920, 1080, 30, 2_000_000),
            Audio: new AudioStreamInfo("aac", 2, 48_000, 128_000));
    }

    private sealed class FakeMediaProbe : IMediaProbe
    {
        private readonly MediaInfo _mediaInfo;

        public FakeMediaProbe(MediaInfo mediaInfo)
        {
            _mediaInfo = mediaInfo;
        }

        public Task<MediaInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_mediaInfo);
        }

        public MediaInfo ParseProbeJson(string ffprobeJson)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeFfmpegEngine : IFfmpegEngine
    {
        private readonly int _exitCode;
        private readonly string _stderr;

        public FakeFfmpegEngine(int exitCode, string stderr = "")
        {
            _exitCode = exitCode;
            _stderr = stderr;
        }

        public string FfmpegPath => "ffmpeg";

        public string FfprobePath => "ffprobe";

        public IReadOnlyList<string> LastFfmpegArgs { get; private set; } = Array.Empty<string>();

        public IReadOnlyList<string> BuildSafeArgumentList(params string[] arguments) => arguments;

        public Task<FfmpegRunResult> RunFfmpegAsync(
            IEnumerable<string> arguments,
            TimeSpan? expectedDuration = null,
            IProgress<FfmpegProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastFfmpegArgs = arguments.ToArray();

            progress?.Report(new FfmpegProgress(TimeSpan.FromSeconds(15), 25, "out_time=00:00:15.000"));

            return Task.FromResult(new FfmpegRunResult(
                ExitCode: _exitCode,
                StdOut: string.Empty,
                StdErr: _stderr,
                WasCanceled: false,
                ProgressEvents: Array.Empty<FfmpegProgress>(),
                StartedAtUtc: DateTimeOffset.UtcNow,
                EndedAtUtc: DateTimeOffset.UtcNow));
        }

        public Task<ProcessRunResult> RunFfprobeAsync(
            IEnumerable<string> arguments,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessRunResult(
                ExitCode: 0,
                StdOut: "{}",
                StdErr: string.Empty,
                WasCanceled: false,
                StartedAtUtc: DateTimeOffset.UtcNow,
                EndedAtUtc: DateTimeOffset.UtcNow));
        }
    }
}
