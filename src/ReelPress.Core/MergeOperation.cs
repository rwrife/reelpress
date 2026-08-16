using System.Text;

namespace ReelPress.Core;

public sealed class MergeOperation : IVideoOperation
{
    private readonly IReadOnlyList<string> _inputPaths;
    private readonly IReadOnlyList<MediaInfo>? _inputMediaInfos;

    public MergeOperation(
        IEnumerable<string> inputPaths,
        MergeMode mode = MergeMode.Auto,
        IReadOnlyList<MediaInfo>? inputMediaInfos = null)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        _inputPaths = inputPaths.ToList();
        _inputMediaInfos = inputMediaInfos;
        Mode = mode;
    }

    public string Name => "merge";

    public MergeMode Mode { get; }

    public IReadOnlyList<string> InputPaths => _inputPaths;

    public IReadOnlyList<string> Validate(MediaInfo mediaInfo)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        var errors = new List<string>();

        if (_inputPaths.Count < 2)
        {
            errors.Add("Merge requires at least two input paths.");
        }

        if (_inputPaths.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Input paths cannot be empty.");
        }

        if (mediaInfo.Video is null)
        {
            errors.Add("Merge requires a video stream.");
        }

        if (_inputMediaInfos is not null && _inputMediaInfos.Count != _inputPaths.Count)
        {
            errors.Add("Input media info count must match input path count when provided.");
        }

        if (Mode == MergeMode.ForceConcatDemuxer && !CanUseConcatDemuxer(mediaInfo))
        {
            errors.Add("Concat demuxer path requires codec-compatible inputs with full media metadata.");
        }

        return errors;
    }

    public void Apply(MediaInfo mediaInfo, VideoOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(context);

        if (ShouldUseConcatDemuxer(mediaInfo))
        {
            var concatListPath = BuildConcatListFile();
            context.AddPreInputArguments("-f", "concat", "-safe", "0");
            context.OverrideInputPath(concatListPath);
            context.AddPostInputArguments("-c", "copy", "-fflags", "+genpts");
            return;
        }

        for (var i = 1; i < _inputPaths.Count; i++)
        {
            context.AddPostInputArguments("-i", _inputPaths[i]);
        }

        var includeAudio = ShouldIncludeAudioForReencode(mediaInfo);
        var filterComplex = BuildConcatFilter(
            inputCount: _inputPaths.Count,
            includeAudio: includeAudio,
            targetWidth: mediaInfo.Video?.Width,
            targetHeight: mediaInfo.Video?.Height);

        context.AddPostInputArguments("-filter_complex", filterComplex, "-map", "[vout]");
        if (includeAudio)
        {
            context.AddPostInputArguments("-map", "[aout]");
        }

        context.AddPostInputArguments("-c:v", "libx264", "-preset", "medium", "-crf", "20");

        if (includeAudio)
        {
            context.AddPostInputArguments("-c:a", "aac", "-b:a", "192k");
        }
        else
        {
            context.AddPostInputArguments("-an");
        }

        context.AddPostInputArguments("-movflags", "+faststart");
    }

    private bool ShouldUseConcatDemuxer(MediaInfo primaryMediaInfo)
    {
        return Mode switch
        {
            MergeMode.ForceConcatDemuxer => true,
            MergeMode.ForceReencode => false,
            _ => CanUseConcatDemuxer(primaryMediaInfo)
        };
    }

    private bool ShouldIncludeAudioForReencode(MediaInfo primaryMediaInfo)
    {
        if (primaryMediaInfo.Audio is null)
        {
            return false;
        }

        if (_inputMediaInfos is null || _inputMediaInfos.Count != _inputPaths.Count)
        {
            return true;
        }

        return _inputMediaInfos.All(info => info.Audio is not null);
    }

    private bool CanUseConcatDemuxer(MediaInfo primaryMediaInfo)
    {
        if (_inputMediaInfos is null || _inputMediaInfos.Count != _inputPaths.Count)
        {
            return false;
        }

        var baseline = _inputMediaInfos[0];

        foreach (var candidate in _inputMediaInfos.Skip(1))
        {
            if (!IsCodecCompatible(baseline, candidate))
            {
                return false;
            }
        }

        return IsCodecCompatible(primaryMediaInfo, baseline);
    }

    private static bool IsCodecCompatible(MediaInfo left, MediaInfo right)
    {
        if (left.Video is null || right.Video is null)
        {
            return false;
        }

        if (!StringEquals(left.Video.Codec, right.Video.Codec)
            || left.Video.Width != right.Video.Width
            || left.Video.Height != right.Video.Height)
        {
            return false;
        }

        var leftHasAudio = left.Audio is not null;
        var rightHasAudio = right.Audio is not null;

        if (leftHasAudio != rightHasAudio)
        {
            return false;
        }

        if (!leftHasAudio)
        {
            return true;
        }

        return StringEquals(left.Audio!.Codec, right.Audio!.Codec)
            && left.Audio.Channels == right.Audio.Channels
            && left.Audio.SampleRate == right.Audio.SampleRate;
    }

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string BuildConcatFilter(
        int inputCount,
        bool includeAudio,
        int? targetWidth,
        int? targetHeight)
    {
        var sections = new List<string>();
        var concatInputs = new StringBuilder();

        for (var i = 0; i < inputCount; i++)
        {
            var videoLabel = $"v{i}";
            var videoFilter = targetWidth is > 0 && targetHeight is > 0
                ? $"[{i}:v:0]setpts=PTS-STARTPTS,scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2,setsar=1[{videoLabel}]"
                : $"[{i}:v:0]setpts=PTS-STARTPTS[{videoLabel}]";

            sections.Add(videoFilter);
            concatInputs.Append($"[{videoLabel}]");

            if (includeAudio)
            {
                var audioLabel = $"a{i}";
                sections.Add($"[{i}:a:0]aresample=async=1:first_pts=0[{audioLabel}]");
                concatInputs.Append($"[{audioLabel}]");
            }
        }

        if (includeAudio)
        {
            sections.Add($"{concatInputs}concat=n={inputCount}:v=1:a=1[vout][aout]");
        }
        else
        {
            sections.Add($"{concatInputs}concat=n={inputCount}:v=1:a=0[vout]");
        }

        return string.Join(';', sections);
    }

    private string BuildConcatListFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"reelpress-concat-{Guid.NewGuid():N}.txt");

        var lines = _inputPaths
            .Select(path => Path.GetFullPath(path))
            .Select(path => $"file '{EscapeForConcatList(path)}'")
            .ToArray();

        File.WriteAllLines(tempPath, lines);
        return tempPath;
    }

    private static string EscapeForConcatList(string path) =>
        path.Replace("'", "'\\''", StringComparison.Ordinal);
}
