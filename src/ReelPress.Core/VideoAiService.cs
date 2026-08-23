using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReelPress.Core;

public sealed partial class VideoAiService : IVideoAiService
{
    private const string FallbackReason = "Local AI unavailable; deterministic fallback used.";
    private readonly HttpClient _httpClient;
    private readonly IVideoAiSettingsStore _settingsStore;

    public VideoAiService(
        HttpMessageHandler httpHandler,
        IVideoAiSettingsStore settingsStore,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpHandler);
        if (httpHandler is HttpClientHandler clientHandler)
        {
            // A 307/308 response must never be able to forward sampled frames to
            // a host that did not pass the loopback-only endpoint validation.
            clientHandler.AllowAutoRedirect = false;
        }

        _httpClient = new HttpClient(httpHandler, disposeHandler: true)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(10)
        };
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public VideoAiSettings Settings { get; private set; } = new();

    public VideoAiStatus Status { get; private set; } = new(VideoAiConnectionState.Disabled, "Local AI is off.");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await ProbeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateSettingsAsync(VideoAiSettings settings, CancellationToken cancellationToken = default)
    {
        Settings = settings with
        {
            Endpoint = settings.Endpoint.Trim(),
            VisionModel = settings.VisionModel.Trim(),
            TextModel = settings.TextModel.Trim()
        };
        await _settingsStore.SaveAsync(Settings, cancellationToken).ConfigureAwait(false);
        Status = Settings.Enabled
            ? new VideoAiStatus(VideoAiConnectionState.Unreachable, "Settings saved; probe the local server.")
            : new VideoAiStatus(VideoAiConnectionState.Disabled, "Local AI is off.");
    }

    public async Task<VideoAiStatus> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (!Settings.Enabled)
        {
            return Status = new(VideoAiConnectionState.Disabled, "Local AI is off; no requests are sent.");
        }

        try
        {
            var routes = ResolveRoutes(Settings.Endpoint);
            using var response = await _httpClient.GetAsync(routes.Probe, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Status = new(VideoAiConnectionState.Unreachable,
                    $"Local AI probe failed (HTTP {(int)response.StatusCode}).");
            }

            return Status = new(VideoAiConnectionState.Reachable, $"Local AI reachable at {routes.DisplayEndpoint}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ArgumentException)
        {
            return Status = new(VideoAiConnectionState.Unreachable, $"Local AI unreachable: {ex.Message}");
        }
    }

    public async Task<ThumbnailSelection> SelectThumbnailAsync(
        IReadOnlyList<VideoFrameCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one candidate frame is required.", nameof(candidates));
        }

        var fallback = candidates[candidates.Count / 2];
        if (!CanRequest())
        {
            return new(fallback, true, FallbackReason);
        }

        try
        {
            var ids = string.Join(", ", candidates.Select(candidate => candidate.Id));
            var prompt = $"Choose the single most representative, clear poster frame. Reply only with one ID from: {ids}.";
            var answer = await SendChatAsync(Settings.VisionModel, prompt, candidates, cancellationToken).ConfigureAwait(false);
            var selected = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, answer.Trim().Trim('`', '"', '\''), StringComparison.OrdinalIgnoreCase));
            return selected is null
                ? new(fallback, true, "Local AI returned an invalid frame ID; center fallback used.")
                : new(selected, false, "Thumbnail selected by the local vision model.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or ArgumentException)
        {
            return new(fallback, true, $"{FallbackReason} {ex.Message}");
        }
    }

    public async Task<TitleSuggestion> SuggestTitleAsync(
        IReadOnlyList<VideoFrameCandidate> sampledFrames,
        VideoTitleMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sampledFrames);
        ArgumentNullException.ThrowIfNull(metadata);
        var fallback = SafeFallbackName(metadata.SourceFileName);
        if (!CanRequest())
        {
            return new(fallback, fallback, true, FallbackReason);
        }

        try
        {
            var summaryPrompt = "Summarize the visible scenes across these sampled video frames factually and concisely. " +
                                "Mention the main subjects, setting, and activity; do not propose a title.";
            var sceneSummary = await SendChatAsync(
                Settings.VisionModel, summaryPrompt, sampledFrames, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sceneSummary))
            {
                throw new JsonException("Local vision model returned an empty scene summary.");
            }

            var prompt = $"Suggest a short factual video title (maximum 8 words). Reply with only the title. " +
                         $"Scene summary: {sceneSummary.Trim()} " +
                         $"Metadata: source={Path.GetFileName(metadata.SourceFileName)}; " +
                         $"duration={metadata.Duration.TotalSeconds:0.###}s; dimensions={metadata.Width}x{metadata.Height}; " +
                         $"recorded={metadata.RecordedAt?.ToString("O") ?? "unknown"}.";
            var answer = await SendChatAsync(Settings.TextModel, prompt, null, cancellationToken).ConfigureAwait(false);
            var title = NormalizeTitle(answer);
            if (string.IsNullOrWhiteSpace(title))
            {
                return new(fallback, fallback, true, "Local AI returned a malformed title; safe filename fallback used.");
            }

            var safeName = ToSafeFileName(title);
            return string.IsNullOrWhiteSpace(safeName)
                ? new(fallback, fallback, true, "Local AI returned an unsafe title; safe filename fallback used.")
                : new(title, safeName, false, "Title suggested by the local model.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or ArgumentException)
        {
            return new(fallback, fallback, true, $"{FallbackReason} {ex.Message}");
        }
    }

    private bool CanRequest() => Settings.Enabled && Status.IsReachable;

    private async Task<string> SendChatAsync(
        string model,
        string prompt,
        IReadOnlyList<VideoFrameCandidate>? frames,
        CancellationToken cancellationToken)
    {
        var routes = ResolveRoutes(Settings.Endpoint);
        object payload;
        if (routes.IsOllamaNative && frames is not null)
        {
            payload = new
            {
                model,
                stream = false,
                options = new { temperature = 0, seed = 0 },
                messages = new[]
                {
                    new { role = "user", content = prompt, images = frames.Select(f => Convert.ToBase64String(f.ImageBytes)).ToArray() }
                }
            };
        }
        else if (routes.IsOllamaNative)
        {
            payload = new
            {
                model,
                stream = false,
                options = new { temperature = 0, seed = 0 },
                messages = new[] { new { role = "user", content = prompt } }
            };
        }
        else if (frames is not null)
        {
            var content = new List<object> { new { type = "text", text = prompt } };
            content.AddRange(frames.Select(frame => (object)new
            {
                type = "image_url",
                image_url = new { url = $"data:{frame.MediaType};base64,{Convert.ToBase64String(frame.ImageBytes)}" }
            }));
            payload = new
            {
                model,
                temperature = 0,
                seed = 0,
                messages = new[] { new { role = "user", content } }
            };
        }
        else
        {
            payload = new
            {
                model,
                temperature = 0,
                seed = 0,
                messages = new[] { new { role = "user", content = prompt } }
            };
        }

        using var response = await _httpClient.PostAsJsonAsync(routes.Chat, payload, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (routes.IsOllamaNative && json.RootElement.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var ollamaContent))
        {
            return ollamaContent.GetString() ?? throw new JsonException("Missing Ollama response content.");
        }

        if (json.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var choiceMessage) &&
            choiceMessage.TryGetProperty("content", out var openAiContent))
        {
            return openAiContent.GetString() ?? throw new JsonException("Missing chat completion content.");
        }

        throw new JsonException("Malformed chat completion response.");
    }

    private static (Uri Probe, Uri Chat, bool IsOllamaNative, string DisplayEndpoint) ResolveRoutes(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !IsLoopbackHost(uri.Host))
        {
            throw new ArgumentException(
                "Endpoint must be an HTTP/HTTPS loopback URL using localhost, 127.0.0.0/8, or ::1.", nameof(endpoint));
        }

        if (uri.AbsolutePath is "" or "/")
        {
            return (new Uri(uri, "/api/tags"), new Uri(uri, "/api/chat"), true, normalized);
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.Equals("/v1", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Unsupported endpoint route. Use the server root for Ollama or /v1 for an OpenAI-compatible server.",
                nameof(endpoint));
        }

        var baseText = path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^"/chat/completions".Length]
            : normalized;
        return (new Uri(baseText + "/models"), new Uri(baseText + "/chat/completions"), false, normalized);
    }

    private static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    private static string NormalizeTitle(string value)
    {
        var firstLine = value.Trim().Trim('`', '"', '\'', ' ', '.', '-').Split('\n', '\r')[0].Trim();
        return firstLine.Length <= 100 ? firstLine : firstLine[..100].Trim();
    }

    private static string SafeFallbackName(string sourceFileName)
    {
        var source = Path.GetFileNameWithoutExtension(sourceFileName);
        var safe = ToSafeFileName(source);
        return string.IsNullOrWhiteSpace(safe) ? "untitled-video" : safe;
    }

    private static string ToSafeFileName(string value)
    {
        var tokens = TokenRegex().Matches(value.ToLowerInvariant()).Select(match => match.Value).Take(12);
        return string.Join('-', tokens).Trim('-');
    }

    [GeneratedRegex("[\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
