namespace ReelPress.Core;

public interface IVideoAiService
{
    VideoAiSettings Settings { get; }

    VideoAiStatus Status { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task UpdateSettingsAsync(VideoAiSettings settings, CancellationToken cancellationToken = default);

    Task<VideoAiStatus> ProbeAsync(CancellationToken cancellationToken = default);

    Task<ThumbnailSelection> SelectThumbnailAsync(
        IReadOnlyList<VideoFrameCandidate> candidates,
        CancellationToken cancellationToken = default);

    Task<TitleSuggestion> SuggestTitleAsync(
        IReadOnlyList<VideoFrameCandidate> sampledFrames,
        VideoTitleMetadata metadata,
        CancellationToken cancellationToken = default);
}
