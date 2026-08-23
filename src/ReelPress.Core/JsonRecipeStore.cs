using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReelPress.Core;

public sealed record Recipe(string Name, IReadOnlyList<IVideoOperation> Operations, int Version = 1);

public interface IRecipeStore
{
    string RootDirectory { get; }

    Task<Recipe> LoadAsync(string nameOrPath, CancellationToken cancellationToken = default);

    Task SaveAsync(string nameOrPath, Recipe recipe, CancellationToken cancellationToken = default);

    IReadOnlyList<string> ListPresets();
}

public sealed class JsonRecipeStore : IRecipeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public JsonRecipeStore(string? rootDirectory = null)
    {
        RootDirectory = Path.GetFullPath(rootDirectory ?? GetDefaultRootDirectory());
    }

    public string RootDirectory { get; }

    public async Task<Recipe> LoadAsync(string nameOrPath, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(nameOrPath);
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<RecipeDocument>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Recipe is empty: {path}");

        if (document.Version != 1)
        {
            throw new InvalidDataException($"Unsupported recipe version {document.Version}. Expected version 1.");
        }

        if (document.Operations is null || document.Operations.Count == 0)
        {
            throw new InvalidDataException("Recipe must contain at least one operation.");
        }

        var operations = document.Operations.Select(ToOperation).ToArray();
        return new Recipe(document.Name ?? Path.GetFileNameWithoutExtension(path), operations, document.Version);
    }

    public async Task SaveAsync(string nameOrPath, Recipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (recipe.Operations is null || recipe.Operations.Count == 0)
        {
            throw new ArgumentException("Recipe must contain at least one operation.", nameof(recipe));
        }

        var path = ResolvePath(nameOrPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var document = new RecipeDocument(
            Version: 1,
            Name: recipe.Name,
            Operations: recipe.Operations.Select(FromOperation).ToList());

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<string> ListPresets()
    {
        if (!Directory.Exists(RootDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(RootDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string GetDefaultRootDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "reelpress");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "reelpress");
        }

        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return Path.Combine(
            string.IsNullOrWhiteSpace(xdgConfigHome)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdgConfigHome,
            "reelpress");
    }

    private string ResolvePath(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            throw new ArgumentException("Recipe name or path is required.", nameof(nameOrPath));
        }

        var value = Environment.ExpandEnvironmentVariables(nameOrPath.Trim());
        var isExplicitPath = Path.IsPathRooted(value)
            || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar)
            || string.Equals(Path.GetExtension(value), ".json", StringComparison.OrdinalIgnoreCase);

        var path = isExplicitPath ? value : Path.Combine(RootDirectory, value);
        if (string.IsNullOrEmpty(Path.GetExtension(path)))
        {
            path += ".json";
        }

        return Path.GetFullPath(path);
    }

    private static RecipeOperationDocument FromOperation(IVideoOperation operation) => operation switch
    {
        TrimOperation value => new("trim")
        {
            Start = value.Start,
            End = value.End,
            Duration = value.Duration,
            TrimMode = value.Mode
        },
        ConvertOperation value => new("convert")
        {
            Container = value.Container,
            VideoCodec = value.VideoCodec,
            AudioCodec = value.AudioCodec
        },
        CompressOperation value => new("compress")
        {
            CompressionMode = value.Mode,
            Crf = value.Crf,
            TargetSizeBytes = value.TargetSizeBytes,
            AudioBitrateKbps = value.AudioBitrateKbps,
            VideoCodec = value.VideoCodec
        },
        ResizeOperation value => new("resize")
        {
            ResizePreset = value.Preset,
            Width = value.Width,
            Height = value.Height,
            ResizeMode = value.Mode,
            AllowUpscale = value.AllowUpscale
        },
        ExtractAudioOperation value => new("audio")
        {
            AudioFormat = value.Format,
            AudioBitrateKbps = value.BitrateKbps
        },
        ExtractFramesOperation value => new("frames")
        {
            EveryInterval = value.EveryInterval,
            AtTimestamp = value.AtTimestamp,
            FrameFormat = value.Format,
            JpegQuality = value.JpegQuality
        },
        ExportAnimationOperation value => new("gif")
        {
            AnimationFormat = value.Format,
            Start = value.Start,
            End = value.End,
            Duration = value.Duration,
            Fps = value.Fps,
            Width = value.Width
        },
        MuteOperation => new("mute"),
        MergeOperation value => new("merge")
        {
            InputPaths = value.InputPaths.ToList(),
            MergeMode = value.Mode
        },
        _ => throw new NotSupportedException($"Operation '{operation.GetType().Name}' cannot be saved in a recipe.")
    };

    private static IVideoOperation ToOperation(RecipeOperationDocument value)
    {
        if (string.IsNullOrWhiteSpace(value.Type))
        {
            throw new InvalidDataException("Recipe operation type is required.");
        }

        return value.Type.Trim().ToLowerInvariant() switch
        {
            "trim" => new TrimOperation(
                value.Start ?? TimeSpan.Zero,
                value.End,
                value.Duration,
                value.TrimMode ?? Core.TrimMode.AutoPreferCopy),
            "convert" => new ConvertOperation(
                Required(value.Container, "container"),
                value.VideoCodec ?? Core.VideoCodec.H264,
                value.AudioCodec ?? Core.AudioCodec.Aac),
            "compress" => new CompressOperation(
                value.CompressionMode ?? Core.CompressionMode.QualityCrf,
                value.Crf ?? 23,
                value.TargetSizeBytes ?? 0,
                value.AudioBitrateKbps ?? 128,
                value.VideoCodec ?? Core.VideoCodec.H264),
            "resize" => new ResizeOperation(
                value.ResizePreset,
                value.Width,
                value.Height,
                value.ResizeMode ?? Core.ResizeMode.Fit,
                value.AllowUpscale ?? false),
            "audio" or "extract-audio" => new ExtractAudioOperation(
                value.AudioFormat ?? AudioExtractionFormat.Mp3,
                value.AudioBitrateKbps ?? 192),
            "frames" or "extract-frames" => new ExtractFramesOperation(
                value.EveryInterval,
                value.AtTimestamp,
                value.FrameFormat ?? FrameImageFormat.Png,
                value.JpegQuality ?? 2),
            "gif" or "animation" or "export-animation" => new ExportAnimationOperation(
                value.AnimationFormat ?? AnimatedImageFormat.Gif,
                value.Start ?? TimeSpan.Zero,
                value.End,
                value.Duration ?? (value.End is null ? TimeSpan.FromSeconds(3) : null),
                value.Fps ?? 15,
                value.Width ?? 480),
            "mute" => new MuteOperation(),
            "merge" => new MergeOperation(
                value.InputPaths ?? throw new InvalidDataException("Merge recipe requires inputPaths."),
                value.MergeMode ?? Core.MergeMode.Auto),
            _ => throw new InvalidDataException($"Unknown recipe operation type '{value.Type}'.")
        };
    }

    private static T Required<T>(T? value, string propertyName) where T : struct =>
        value ?? throw new InvalidDataException($"Recipe operation property '{propertyName}' is required.");

    private sealed record RecipeDocument(int Version, string? Name, List<RecipeOperationDocument> Operations);

    private sealed record RecipeOperationDocument(string Type)
    {
        public TimeSpan? Start { get; init; }
        public TimeSpan? End { get; init; }
        public TimeSpan? Duration { get; init; }
        public TrimMode? TrimMode { get; init; }
        public VideoContainer? Container { get; init; }
        public VideoCodec? VideoCodec { get; init; }
        public AudioCodec? AudioCodec { get; init; }
        public CompressionMode? CompressionMode { get; init; }
        public int? Crf { get; init; }
        public long? TargetSizeBytes { get; init; }
        public int? AudioBitrateKbps { get; init; }
        public ResizePreset? ResizePreset { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public ResizeMode? ResizeMode { get; init; }
        public bool? AllowUpscale { get; init; }
        public AudioExtractionFormat? AudioFormat { get; init; }
        public TimeSpan? EveryInterval { get; init; }
        public TimeSpan? AtTimestamp { get; init; }
        public FrameImageFormat? FrameFormat { get; init; }
        public int? JpegQuality { get; init; }
        public AnimatedImageFormat? AnimationFormat { get; init; }
        public int? Fps { get; init; }
        public List<string>? InputPaths { get; init; }
        public MergeMode? MergeMode { get; init; }
    }
}
