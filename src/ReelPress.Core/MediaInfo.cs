namespace ReelPress.Core;

public sealed record MediaInfo(
    string? Container,
    TimeSpan Duration,
    long? Bitrate,
    IReadOnlyList<MediaStreamInfo> Streams,
    VideoStreamInfo? Video,
    AudioStreamInfo? Audio);

public sealed record MediaStreamInfo(
    int Index,
    string? CodecType,
    string? CodecName,
    long? Bitrate,
    int? Width,
    int? Height,
    double? Fps,
    int? Channels,
    int? SampleRate);

public sealed record VideoStreamInfo(
    string? Codec,
    int? Width,
    int? Height,
    double? Fps,
    long? Bitrate);

public sealed record AudioStreamInfo(
    string? Codec,
    int? Channels,
    int? SampleRate,
    long? Bitrate);
