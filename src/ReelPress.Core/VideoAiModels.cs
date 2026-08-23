namespace ReelPress.Core;

public sealed record VideoAiSettings(
    bool Enabled = false,
    string Endpoint = "http://localhost:11434",
    string VisionModel = "minicpm-v",
    string TextModel = "llama3.2");

public enum VideoAiConnectionState
{
    Disabled,
    Reachable,
    Unreachable
}

public sealed record VideoAiStatus(VideoAiConnectionState State, string Message)
{
    public bool IsReachable => State == VideoAiConnectionState.Reachable;
}

public sealed record VideoFrameCandidate(string Id, byte[] ImageBytes, string MediaType = "image/jpeg");

public sealed record VideoTitleMetadata(
    string SourceFileName,
    TimeSpan Duration,
    int Width = 0,
    int Height = 0,
    DateTimeOffset? RecordedAt = null);

public sealed record ThumbnailSelection(VideoFrameCandidate Candidate, bool UsedFallback, string Message);

public sealed record TitleSuggestion(string Title, string SafeFileName, bool UsedFallback, string Message);
