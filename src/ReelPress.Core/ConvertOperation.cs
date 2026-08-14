namespace ReelPress.Core;

public sealed class ConvertOperation : IVideoOperation
{
    public ConvertOperation(
        VideoContainer container,
        VideoCodec videoCodec,
        AudioCodec audioCodec)
    {
        Container = container;
        VideoCodec = videoCodec;
        AudioCodec = audioCodec;
    }

    public string Name => "convert";

    public VideoContainer Container { get; }

    public VideoCodec VideoCodec { get; }

    public AudioCodec AudioCodec { get; }

    public IReadOnlyList<string> Validate(MediaInfo mediaInfo)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        var errors = new List<string>();

        if (mediaInfo.Video is null && VideoCodec != VideoCodec.Copy)
        {
            errors.Add("Requested video re-encode, but source has no video stream.");
        }

        if (AudioCodec != AudioCodec.None && mediaInfo.Audio is null && AudioCodec != AudioCodec.Copy)
        {
            errors.Add("Requested audio encode, but source has no audio stream.");
        }

        if (Container == VideoContainer.WebM)
        {
            if (VideoCodec is not (VideoCodec.VP9 or VideoCodec.AV1 or VideoCodec.Copy))
            {
                errors.Add("WebM requires VP9/AV1 video (or copy from a compatible source).");
            }

            if (AudioCodec is not (AudioCodec.Opus or AudioCodec.None or AudioCodec.Copy))
            {
                errors.Add("WebM requires Opus audio (or no audio/copy from compatible source).");
            }
        }

        return errors;
    }

    public void Apply(MediaInfo mediaInfo, VideoOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(context);

        context.AddPostInputArguments("-f", CodecMaps.ToContainerName(Container));

        if (mediaInfo.Video is not null)
        {
            context.AddPostInputArguments("-c:v", CodecMaps.ToVideoEncoder(VideoCodec));
        }

        switch (AudioCodec)
        {
            case AudioCodec.None:
                context.AddPostInputArguments("-an");
                break;

            case AudioCodec.Copy:
                if (mediaInfo.Audio is not null)
                {
                    context.AddPostInputArguments("-c:a", "copy");
                }
                break;

            default:
                if (mediaInfo.Audio is not null)
                {
                    context.AddPostInputArguments("-c:a", CodecMaps.ToAudioEncoder(AudioCodec));
                }
                break;
        }

        if (Container == VideoContainer.Mp4)
        {
            context.AddPostInputArguments("-movflags", "+faststart");
        }
    }
}
