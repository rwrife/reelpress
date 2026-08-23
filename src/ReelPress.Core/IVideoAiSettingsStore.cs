namespace ReelPress.Core;

public interface IVideoAiSettingsStore
{
    Task<VideoAiSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(VideoAiSettings settings, CancellationToken cancellationToken = default);
}
