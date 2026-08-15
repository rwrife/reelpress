namespace ReelPress.Core;

public sealed class ExtractAudioOperation : IVideoOperation
{
    public ExtractAudioOperation(AudioExtractionFormat format, int bitrateKbps = 192)
    {
        Format = format;
        BitrateKbps = bitrateKbps;
    }

    public string Name => "extract-audio";

    public AudioExtractionFormat Format { get; }

    public int BitrateKbps { get; }

    public IReadOnlyList<string> Validate(MediaInfo mediaInfo)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        var errors = new List<string>();

        if (mediaInfo.Audio is null)
        {
            errors.Add("Audio extraction requires an audio stream.");
        }

        if ((Format is AudioExtractionFormat.Mp3 or AudioExtractionFormat.Aac) && BitrateKbps <= 0)
        {
            errors.Add("Bitrate must be greater than zero for lossy audio formats.");
        }

        return errors;
    }

    public void Apply(MediaInfo mediaInfo, VideoOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(context);

        context.AddPostInputArguments("-map", "0:a:0", "-vn");

        switch (Format)
        {
            case AudioExtractionFormat.Mp3:
                context.AddPostInputArguments("-c:a", "libmp3lame", "-b:a", $"{BitrateKbps}k");
                break;

            case AudioExtractionFormat.Aac:
                context.AddPostInputArguments("-c:a", "aac", "-b:a", $"{BitrateKbps}k");
                break;

            case AudioExtractionFormat.Wav:
                context.AddPostInputArguments("-c:a", "pcm_s16le");
                break;

            case AudioExtractionFormat.Flac:
                context.AddPostInputArguments("-c:a", "flac");
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
