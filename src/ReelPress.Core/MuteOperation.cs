namespace ReelPress.Core;

public sealed class MuteOperation : IVideoOperation
{
    public string Name => "mute";

    public IReadOnlyList<string> Validate(MediaInfo mediaInfo)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        var errors = new List<string>();

        if (mediaInfo.Video is null)
        {
            errors.Add("Mute requires a video stream.");
        }

        if (mediaInfo.Audio is null)
        {
            errors.Add("Source has no audio stream to mute.");
        }

        return errors;
    }

    public void Apply(MediaInfo mediaInfo, VideoOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(context);

        context.AddPostInputArguments("-an");
        context.AddPostInputArguments("-c:v", "copy");
    }
}
