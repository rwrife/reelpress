using System.Collections.Concurrent;
using System.Text.Json;

namespace ReelPress.Core;

public sealed class JsonVideoAiSettingsStore : IVideoAiSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteLocks = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly SemaphoreSlim _writeLock;

    public JsonVideoAiSettingsStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(GetSettingsDirectory(), "settings.json"));
        _writeLock = WriteLocks.GetOrAdd(_path, static _ => new SemaphoreSlim(1, 1));
    }

    public async Task<VideoAiSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new VideoAiSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(
                stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            return Normalize(document?.LocalAi ?? new VideoAiSettings());
        }
        catch (JsonException)
        {
            return new VideoAiSettings();
        }
        catch (IOException)
        {
            return new VideoAiSettings();
        }
    }

    public async Task SaveAsync(VideoAiSettings settings, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporaryPath = _path + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    stream, new SettingsDocument(Normalize(settings)), SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // A failed cleanup must not hide the original save result.
            }
            _writeLock.Release();
        }
    }

    private static VideoAiSettings Normalize(VideoAiSettings settings) => settings with
    {
        Endpoint = string.IsNullOrWhiteSpace(settings.Endpoint) ? "http://localhost:11434" : settings.Endpoint.Trim(),
        VisionModel = string.IsNullOrWhiteSpace(settings.VisionModel) ? "minicpm-v" : settings.VisionModel.Trim(),
        TextModel = string.IsNullOrWhiteSpace(settings.TextModel) ? "llama3.2" : settings.TextModel.Trim()
    };

    private static string GetSettingsDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(root, "reelpress");
    }

    private sealed record SettingsDocument(VideoAiSettings LocalAi);
}
