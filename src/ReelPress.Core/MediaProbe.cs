using System.Globalization;
using System.Text.Json;

namespace ReelPress.Core;

public sealed class MediaProbe : IMediaProbe
{
    private readonly IFfmpegEngine _ffmpegEngine;

    public MediaProbe(IFfmpegEngine ffmpegEngine)
    {
        _ffmpegEngine = ffmpegEngine ?? throw new ArgumentNullException(nameof(ffmpegEngine));
    }

    public async Task<MediaInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }

        var arguments = _ffmpegEngine.BuildSafeArgumentList(
            "-v", "error",
            "-print_format", "json",
            "-show_format",
            "-show_streams",
            inputPath);

        var result = await _ffmpegEngine.RunFfprobeAsync(arguments, cancellationToken).ConfigureAwait(false);

        if (result.WasCanceled)
        {
            throw new OperationCanceledException("ffprobe execution was canceled.");
        }

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"ffprobe failed with exit code {result.ExitCode}: {result.StdErr.Trim()}");
        }

        return ParseProbeJson(result.StdOut);
    }

    public MediaInfo ParseProbeJson(string ffprobeJson)
    {
        if (string.IsNullOrWhiteSpace(ffprobeJson))
        {
            throw new ArgumentException("ffprobe JSON payload is empty.", nameof(ffprobeJson));
        }

        using var document = JsonDocument.Parse(ffprobeJson);
        var root = document.RootElement;

        var format = root.TryGetProperty("format", out var formatElement)
            ? formatElement
            : default;

        var container = TryGetString(format, "format_name")?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        var duration = ParseDuration(TryGetString(format, "duration"));
        var bitrate = ParseNullableLong(TryGetString(format, "bit_rate"));

        var streams = new List<MediaStreamInfo>();

        if (root.TryGetProperty("streams", out var streamArray) && streamArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streamArray.EnumerateArray())
            {
                var codecType = TryGetString(stream, "codec_type");
                var mediaStream = new MediaStreamInfo(
                    Index: ParseNullableInt(TryGetString(stream, "index")) ?? -1,
                    CodecType: codecType,
                    CodecName: TryGetString(stream, "codec_name"),
                    Bitrate: ParseNullableLong(TryGetString(stream, "bit_rate")),
                    Width: ParseNullableInt(TryGetString(stream, "width")),
                    Height: ParseNullableInt(TryGetString(stream, "height")),
                    Fps: ParseFps(TryGetString(stream, "r_frame_rate")),
                    Channels: ParseNullableInt(TryGetString(stream, "channels")),
                    SampleRate: ParseNullableInt(TryGetString(stream, "sample_rate")));

                streams.Add(mediaStream);
            }
        }

        var firstVideo = streams.FirstOrDefault(stream =>
            string.Equals(stream.CodecType, "video", StringComparison.OrdinalIgnoreCase));

        var firstAudio = streams.FirstOrDefault(stream =>
            string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase));

        var video = firstVideo is null
            ? null
            : new VideoStreamInfo(
                firstVideo.CodecName,
                firstVideo.Width,
                firstVideo.Height,
                firstVideo.Fps,
                firstVideo.Bitrate);

        var audio = firstAudio is null
            ? null
            : new AudioStreamInfo(
                firstAudio.CodecName,
                firstAudio.Channels,
                firstAudio.SampleRate,
                firstAudio.Bitrate);

        return new MediaInfo(
            Container: container,
            Duration: duration,
            Bitrate: bitrate,
            Streams: streams,
            Video: video,
            Audio: audio);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static TimeSpan ParseDuration(string? rawDuration)
    {
        if (string.IsNullOrWhiteSpace(rawDuration))
        {
            return TimeSpan.Zero;
        }

        if (double.TryParse(rawDuration, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.Zero;
    }

    private static int? ParseNullableInt(string? value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static long? ParseNullableLong(string? value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double? ParseFps(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw.Contains('/', StringComparison.Ordinal))
        {
            var parts = raw.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
                && denominator != 0)
            {
                return numerator / denominator;
            }
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps))
        {
            return fps;
        }

        return null;
    }
}
