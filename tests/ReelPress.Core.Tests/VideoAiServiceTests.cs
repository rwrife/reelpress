using System.Net;
using System.Text;
using System.Text.Json;
using ReelPress.Core;

namespace ReelPress.Core.Tests;

public sealed class VideoAiServiceTests
{
    [Fact]
    public async Task Disabled_UsesDeterministicFallbacksWithoutHttpRequests()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var service = CreateService(handler, new VideoAiSettings());
        await service.InitializeAsync();
        var frames = Frames(3);

        var thumbnail = await service.SelectThumbnailAsync(frames);
        var title = await service.SuggestTitleAsync(frames, new VideoTitleMetadata("My Summer Trip!.mp4", TimeSpan.FromSeconds(12)));

        Assert.Equal(VideoAiConnectionState.Disabled, service.Status.State);
        Assert.True(thumbnail.UsedFallback);
        Assert.Equal("frame-2", thumbnail.Candidate.Id);
        Assert.True(title.UsedFallback);
        Assert.Equal("my-summer-trip", title.SafeFileName);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task UnreachableProbe_HasClearStatusAndCenterFallback()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var service = CreateService(handler, EnabledSettings());

        await service.InitializeAsync();
        var selection = await service.SelectThumbnailAsync(Frames(4));

        Assert.Equal(VideoAiConnectionState.Unreachable, service.Status.State);
        Assert.Contains("connection refused", service.Status.Message);
        Assert.True(selection.UsedFallback);
        Assert.Equal("frame-3", selection.Candidate.Id);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task NonCallerCancellation_IsReportedAsUnreachable()
    {
        var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("request timed out"));
        var service = CreateService(handler, EnabledSettings());

        await service.InitializeAsync();

        Assert.Equal(VideoAiConnectionState.Unreachable, service.Status.State);
        Assert.Contains("request timed out", service.Status.Message);
    }

    [Fact]
    public async Task OllamaNative_ProbesAndParsesThumbnailAndTitleEndToEnd()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            JsonResponse("{\"models\":[]}"),
            JsonResponse("{\"message\":{\"role\":\"assistant\",\"content\":\"frame-3\"}}"),
            JsonResponse("{\"message\":{\"role\":\"assistant\",\"content\":\"A sunrise over a beach\"}}"),
            JsonResponse("{\"message\":{\"role\":\"assistant\",\"content\":\"Golden Beach Sunrise\"}}")
        ]);
        var handler = new StubHttpMessageHandler(_ => responses.Dequeue());
        var service = CreateService(handler, EnabledSettings());

        await service.InitializeAsync();
        var selection = await service.SelectThumbnailAsync(Frames(3));
        var title = await service.SuggestTitleAsync(
            Frames(2), new VideoTitleMetadata("raw.mov", TimeSpan.FromSeconds(42), 1920, 1080));

        Assert.True(service.Status.IsReachable);
        Assert.False(selection.UsedFallback);
        Assert.Equal("frame-3", selection.Candidate.Id);
        Assert.False(title.UsedFallback);
        Assert.Equal("Golden Beach Sunrise", title.Title);
        Assert.Equal("golden-beach-sunrise", title.SafeFileName);
        Assert.Equal("/api/tags", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.All(handler.Requests.Skip(1), request => Assert.Equal("/api/chat", request.RequestUri!.AbsolutePath));

        using var request = JsonDocument.Parse(handler.Bodies[1]);
        Assert.Equal("minicpm-v", request.RootElement.GetProperty("model").GetString());
        var images = request.RootElement.GetProperty("messages")[0].GetProperty("images");
        Assert.Equal(3, images.GetArrayLength());
        Assert.DoesNotContain("raw.mov", handler.Bodies[0]);
        using var summaryRequest = JsonDocument.Parse(handler.Bodies[2]);
        Assert.Equal("minicpm-v", summaryRequest.RootElement.GetProperty("model").GetString());
        Assert.Equal(2, summaryRequest.RootElement.GetProperty("messages")[0].GetProperty("images").GetArrayLength());
        using var titleRequest = JsonDocument.Parse(handler.Bodies[3]);
        Assert.Equal("llama3.2", titleRequest.RootElement.GetProperty("model").GetString());
        Assert.False(titleRequest.RootElement.GetProperty("messages")[0].TryGetProperty("images", out _));
        Assert.Contains("A sunrise over a beach", handler.Bodies[3]);
    }

    [Fact]
    public async Task OpenAiCompatibleEndpoint_UsesV1RoutesAndDataUrls()
    {
        var handler = new StubHttpMessageHandler(request => request.Method == HttpMethod.Get
            ? JsonResponse("{\"data\":[]}")
            : JsonResponse("{\"choices\":[{\"message\":{\"content\":\"frame-1\"}}]}"));
        var settings = EnabledSettings() with { Endpoint = "http://127.0.0.1:8080/v1" };
        var service = CreateService(handler, settings);

        await service.InitializeAsync();
        var result = await service.SelectThumbnailAsync(Frames(2));

        Assert.False(result.UsedFallback);
        Assert.Equal("/v1/models", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/v1/chat/completions", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Contains("data:image/jpeg;base64,", handler.Bodies[1]);
    }

    [Fact]
    public async Task OpenAiCompatibleTitle_UsesVisionImagesThenTextOnlyRequest()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            JsonResponse("{\"data\":[]}"),
            JsonResponse("{\"choices\":[{\"message\":{\"content\":\"A dog runs through snow\"}}]}"),
            JsonResponse("{\"choices\":[{\"message\":{\"content\":\"Dog Running Through Snow\"}}]}")
        ]);
        var handler = new StubHttpMessageHandler(_ => responses.Dequeue());
        var service = CreateService(handler, EnabledSettings() with { Endpoint = "http://[::1]:8080/v1" });

        await service.InitializeAsync();
        var result = await service.SuggestTitleAsync(
            Frames(2), new VideoTitleMetadata("clip.mp4", TimeSpan.FromSeconds(8), 1280, 720));

        Assert.False(result.UsedFallback);
        Assert.Contains("image_url", handler.Bodies[1]);
        Assert.DoesNotContain("image_url", handler.Bodies[2]);
        Assert.DoesNotContain("data:image", handler.Bodies[2]);
        Assert.Contains("A dog runs through snow", handler.Bodies[2]);
        using var titleRequest = JsonDocument.Parse(handler.Bodies[2]);
        Assert.Equal(JsonValueKind.String,
            titleRequest.RootElement.GetProperty("messages")[0].GetProperty("content").ValueKind);
    }

    [Theory]
    [InlineData("https://example.com/v1")]
    [InlineData("ftp://localhost:11434")]
    [InlineData("not a uri")]
    [InlineData("http://localhost:8080/completion")]
    public async Task InvalidOrNonLoopbackEndpoint_IsUnreachableWithoutHttp(string endpoint)
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var service = CreateService(handler, EnabledSettings() with { Endpoint = endpoint });

        await service.InitializeAsync();
        var title = await service.SuggestTitleAsync(
            Frames(1), new VideoTitleMetadata("safe.mp4", TimeSpan.Zero));

        Assert.Equal(VideoAiConnectionState.Unreachable, service.Status.State);
        Assert.True(title.UsedFallback);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task RedirectResponse_IsRejectedWithoutFollowing()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
        redirect.Headers.Location = new Uri("https://example.com/v1/chat/completions");
        var handler = new StubHttpMessageHandler(_ => redirect);
        var service = CreateService(handler, EnabledSettings());

        await service.InitializeAsync();

        Assert.Equal(VideoAiConnectionState.Unreachable, service.Status.State);
        Assert.Contains("HTTP 307", service.Status.Message);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void Constructor_DisablesAutomaticRedirects()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };

        _ = new VideoAiService(handler, new MemorySettingsStore(new VideoAiSettings()));

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task Probe_PropagatesCallerCancellation()
    {
        var handler = new CancelingHttpMessageHandler();
        var service = CreateService(handler, EnabledSettings());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InitializeAsync(cancellation.Token));
    }

    [Fact]
    public async Task Thumbnail_PropagatesCallerCancellation()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{\"models\":[]}"));
        var service = CreateService(handler, EnabledSettings());
        await service.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SelectThumbnailAsync(Frames(1), cancellation.Token));
    }

    [Fact]
    public async Task Title_PropagatesCallerCancellation()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{\"models\":[]}"));
        var service = CreateService(handler, EnabledSettings());
        await service.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SuggestTitleAsync(
            Frames(1), new VideoTitleMetadata("clip.mp4", TimeSpan.Zero), cancellation.Token));
    }

    [Fact]
    public async Task MalformedResponses_UseDeterministicFallbacks()
    {
        var responses = new Queue<HttpResponseMessage>(
        [JsonResponse("{}"), JsonResponse("{\"unexpected\":true}"), JsonResponse("not-json")]);
        var handler = new StubHttpMessageHandler(_ => responses.Dequeue());
        var service = CreateService(handler, EnabledSettings());
        await service.InitializeAsync();

        var thumbnail = await service.SelectThumbnailAsync(Frames(3));
        var title = await service.SuggestTitleAsync(
            Frames(1), new VideoTitleMetadata("2026_08 Product Demo.mp4", TimeSpan.Zero));

        Assert.True(thumbnail.UsedFallback);
        Assert.Equal("frame-2", thumbnail.Candidate.Id);
        Assert.True(title.UsedFallback);
        Assert.Equal("2026-08-product-demo", title.SafeFileName);
    }

    [Fact]
    public async Task JsonSettingsStore_PersistsEnabledEndpointAndModels()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"reelpress-ai-test-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new JsonVideoAiSettingsStore(path);
            var expected = new VideoAiSettings(true, "http://127.0.0.1:9000/v1", "qwen2.5-vl", "phi-3-mini");

            await store.SaveAsync(expected);
            var actual = await new JsonVideoAiSettingsStore(path).LoadAsync();

            Assert.Equal(expected, actual);
            Assert.Contains("\"Enabled\": true", await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task JsonSettingsStore_SerializesConcurrentAtomicWrites()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"reelpress-ai-race-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var stores = Enumerable.Range(0, 3).Select(_ => new JsonVideoAiSettingsStore(path)).ToArray();
            var settings = Enumerable.Range(0, 30)
                .Select(index => EnabledSettings() with { TextModel = $"model-{index}" })
                .ToArray();

            await Task.WhenAll(settings.Select((value, index) => stores[index % stores.Length].SaveAsync(value)));
            var actual = await stores[0].LoadAsync();

            Assert.Contains(actual, settings);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static VideoAiService CreateService(HttpMessageHandler handler, VideoAiSettings settings) =>
        new(handler, new MemorySettingsStore(settings));

    private static VideoAiSettings EnabledSettings() => new(true, "http://localhost:11434", "minicpm-v", "llama3.2");

    private static IReadOnlyList<VideoFrameCandidate> Frames(int count) => Enumerable.Range(1, count)
        .Select(index => new VideoFrameCandidate($"frame-{index}", [(byte)index, 2, 3]))
        .ToArray();

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class MemorySettingsStore(VideoAiSettings settings) : IVideoAiSettingsStore
    {
        private VideoAiSettings _settings = settings;

        public Task<VideoAiSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);

        public Task SaveAsync(VideoAiSettings value, CancellationToken cancellationToken = default)
        {
            _settings = value;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return responder(request);
        }
    }

    private sealed class CancelingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }
}
