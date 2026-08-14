namespace ReelPress.Core;

internal static class CodecMaps
{
    public static string ToVideoEncoder(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "libx264",
        VideoCodec.H265 => "libx265",
        VideoCodec.VP9 => "libvpx-vp9",
        VideoCodec.AV1 => "libaom-av1",
        VideoCodec.Copy => "copy",
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported video codec.")
    };

    public static string ToAudioEncoder(AudioCodec codec) => codec switch
    {
        AudioCodec.Aac => "aac",
        AudioCodec.Opus => "libopus",
        AudioCodec.Mp3 => "libmp3lame",
        AudioCodec.Copy => "copy",
        AudioCodec.None => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported audio codec.")
    };

    public static string ToContainerName(VideoContainer container) => container switch
    {
        VideoContainer.Mp4 => "mp4",
        VideoContainer.Mkv => "matroska",
        VideoContainer.WebM => "webm",
        VideoContainer.Mov => "mov",
        _ => throw new ArgumentOutOfRangeException(nameof(container), container, "Unsupported output container.")
    };
}
