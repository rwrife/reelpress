namespace ReelPress.Core;

public interface IVideoOperation
{
    string Name { get; }

    IReadOnlyList<string> Validate(MediaInfo mediaInfo);

    void Apply(MediaInfo mediaInfo, VideoOperationContext context);
}
