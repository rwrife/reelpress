namespace ReelPress.Core.Tests;

public sealed class MediaProbeTests
{
    [Fact]
    public void ParseProbeJson_MapsNormalizedMediaInfo()
    {
        var payload = ReadFixture("ffprobe-sample.json");
        var probe = new MediaProbe(new StubFfmpegEngine(payload));

        var mediaInfo = probe.ParseProbeJson(payload);

        Assert.Equal("mov", mediaInfo.Container);
        Assert.InRange(
            (mediaInfo.Duration - TimeSpan.FromSeconds(12.345)).Duration(),
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1));
        Assert.Equal(2015400, mediaInfo.Bitrate);
        Assert.Equal(2, mediaInfo.Streams.Count);

        Assert.NotNull(mediaInfo.Video);
        Assert.Equal("h264", mediaInfo.Video!.Codec);
        Assert.Equal(1920, mediaInfo.Video.Width);
        Assert.Equal(1080, mediaInfo.Video.Height);
        Assert.InRange(mediaInfo.Video.Fps ?? 0, 29.96, 29.98);

        Assert.NotNull(mediaInfo.Audio);
        Assert.Equal("aac", mediaInfo.Audio!.Codec);
        Assert.Equal(2, mediaInfo.Audio.Channels);
        Assert.Equal(48_000, mediaInfo.Audio.SampleRate);
    }

    [Fact]
    public async Task ProbeAsync_BuildsFfprobeArgsAndParsesResult()
    {
        var payload = ReadFixture("ffprobe-sample.json");
        var engine = new StubFfmpegEngine(payload);
        var probe = new MediaProbe(engine);

        var mediaInfo = await probe.ProbeAsync("input.mp4");

        Assert.Equal("mov", mediaInfo.Container);
        Assert.Equal(new[]
        {
            "-v", "error",
            "-print_format", "json",
            "-show_format",
            "-show_streams",
            "input.mp4"
        }, engine.LastFfprobeArguments);
    }

    private static string ReadFixture(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        return File.ReadAllText(fixturePath);
    }

    private sealed class StubFfmpegEngine : IFfmpegEngine
    {
        private readonly string _payload;

        public StubFfmpegEngine(string payload)
        {
            _payload = payload;
        }

        public string FfmpegPath => "ffmpeg";

        public string FfprobePath => "ffprobe";

        public IReadOnlyList<string> LastFfprobeArguments { get; private set; } = Array.Empty<string>();

        public IReadOnlyList<string> BuildSafeArgumentList(params string[] arguments) => arguments.ToArray();

        public Task<FfmpegRunResult> RunFfmpegAsync(
            IEnumerable<string> arguments,
            TimeSpan? expectedDuration = null,
            IProgress<FfmpegProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ProcessRunResult> RunFfprobeAsync(
            IEnumerable<string> arguments,
            CancellationToken cancellationToken = default)
        {
            LastFfprobeArguments = arguments.ToArray();
            return Task.FromResult(new ProcessRunResult(
                ExitCode: 0,
                StdOut: _payload,
                StdErr: string.Empty,
                WasCanceled: false,
                StartedAtUtc: DateTimeOffset.UtcNow,
                EndedAtUtc: DateTimeOffset.UtcNow));
        }
    }
}
