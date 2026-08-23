using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReelPress.Core;

namespace ReelPress.Cli;

public sealed class CliApplication
{
    public const int SuccessExitCode = 0;
    public const int ProcessingErrorExitCode = 1;
    public const int UsageErrorExitCode = 2;

    private static readonly HashSet<string> SupportedMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".webm", ".avi", ".m4v"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IFfmpegEngine _engine;
    private readonly IMediaProbe _probe;
    private readonly IRecipeStore _recipeStore;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CliApplication(
        IFfmpegEngine engine,
        IMediaProbe probe,
        IRecipeStore recipeStore,
        TextWriter output,
        TextWriter error)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _recipeStore = recipeStore ?? throw new ArgumentNullException(nameof(recipeStore));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        if (args.Count == 0 || args[0] is "--help" or "-h" or "help")
        {
            await _output.WriteLineAsync(HelpText).ConfigureAwait(false);
            return SuccessExitCode;
        }

        var verb = args[0].ToLowerInvariant();
        if (args.Skip(1).Any(value => value is "--help" or "-h"))
        {
            if (!CommandHelp.TryGetValue(verb, out var commandHelp))
            {
                await _error.WriteLineAsync($"Unknown command '{args[0]}'. Run 'reelpress --help'.").ConfigureAwait(false);
                return UsageErrorExitCode;
            }

            await _output.WriteLineAsync(commandHelp).ConfigureAwait(false);
            return SuccessExitCode;
        }

        try
        {
            var commandArgs = CommandArguments.Parse(args.Skip(1));
            ValidateOptions(verb, commandArgs);
            return verb switch
            {
                "probe" => await RunProbeAsync(commandArgs, cancellationToken).ConfigureAwait(false),
                "run" => await RunRecipeAsync(commandArgs, cancellationToken).ConfigureAwait(false),
                "trim" or "convert" or "compress" or "resize" or "audio" or "frames" or "gif" or "merge" =>
                    await RunStandaloneAsync(verb, commandArgs, cancellationToken).ConfigureAwait(false),
                _ => await UnknownCommandAsync(args[0]).ConfigureAwait(false)
            };
        }
        catch (CliUsageException ex)
        {
            await _error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return UsageErrorExitCode;
        }
        catch (Exception ex)
        {
            await _error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return ProcessingErrorExitCode;
        }
    }

    private async Task<int> RunProbeAsync(CommandArguments args, CancellationToken cancellationToken)
    {
        var inputPath = args.RequirePositional(0, "probe requires an input path.");
        var mediaInfo = await _probe.ProbeAsync(inputPath, cancellationToken).ConfigureAwait(false);

        if (args.HasFlag("json"))
        {
            var normalized = new
            {
                mediaInfo.Container,
                DurationSeconds = mediaInfo.Duration.TotalSeconds,
                mediaInfo.Bitrate,
                mediaInfo.Video,
                mediaInfo.Audio,
                mediaInfo.Streams
            };
            await _output.WriteLineAsync(JsonSerializer.Serialize(normalized, JsonOptions)).ConfigureAwait(false);
        }
        else
        {
            await _output.WriteLineAsync(
                $"{mediaInfo.Container ?? "unknown"} · {mediaInfo.Duration} · {mediaInfo.Video?.Width}x{mediaInfo.Video?.Height}")
                .ConfigureAwait(false);
        }

        return SuccessExitCode;
    }

    private async Task<int> RunRecipeAsync(CommandArguments args, CancellationToken cancellationToken)
    {
        var recipePath = args.RequireValue("recipe", "run requires --recipe <path-or-preset>.");
        var sourcePath = args.RequirePositional(0, "run requires an input file or folder.");
        var outputDirectory = args.RequireValue("out", "run requires --out <directory>.");
        var recipe = await _recipeStore.LoadAsync(recipePath, cancellationToken).ConfigureAwait(false);
        var operations = await RecipeRuntime
            .PrepareOperationsAsync(recipe.Operations, _probe, cancellationToken)
            .ConfigureAwait(false);
        var inputs = ResolveInputs(sourcePath);

        if (inputs.Count == 0)
        {
            throw new CliUsageException($"No supported media files found at '{sourcePath}'.");
        }

        Directory.CreateDirectory(outputDirectory);
        var outputFileNames = ResolveOutputFileNames(inputs, operations);
        var jobs = inputs.Select((input, index) => new BatchJob(
            input,
            Path.Combine(outputDirectory, outputFileNames[index]),
            operations)).ToArray();

        return await RunJobsAsync(jobs, args.HasFlag("json"), cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RunStandaloneAsync(string verb, CommandArguments args, CancellationToken cancellationToken)
    {
        if (verb == "audio" && args.Positionals.Count > 0 && string.Equals(args.Positionals[0], "extract", StringComparison.OrdinalIgnoreCase))
        {
            args = args.WithoutFirstPositional();
        }

        var inputPath = args.RequirePositional(0, $"{verb} requires an input path.");
        var outputPath = args.RequireValue("out", $"{verb} requires --out <path>.");
        IVideoOperation operation = verb switch
        {
            "trim" => BuildTrim(args),
            "convert" => BuildConvert(args),
            "compress" => BuildCompress(args),
            "resize" => BuildResize(args),
            "audio" => BuildAudio(args),
            "frames" => BuildFrames(args),
            "gif" => BuildAnimation(args),
            "merge" => await BuildMergeAsync(args, cancellationToken).ConfigureAwait(false),
            _ => throw new CliUsageException($"Unsupported command '{verb}'.")
        };

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var job = new BatchJob(inputPath, outputPath, new[] { operation });
        return await RunJobsAsync(new[] { job }, args.HasFlag("json"), cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RunJobsAsync(
        IReadOnlyList<BatchJob> jobs,
        bool json,
        CancellationToken cancellationToken)
    {
        var runner = new PipelineRunner(_engine, _probe);
        var results = await runner.RunAsync(jobs, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (json)
        {
            await _output.WriteLineAsync(JsonSerializer.Serialize(results, JsonOptions)).ConfigureAwait(false);
        }
        else
        {
            foreach (var result in results)
            {
                await _output.WriteLineAsync($"{result.Status}: {result.InputPath} -> {result.OutputPath} ({result.Message})")
                    .ConfigureAwait(false);
            }
        }

        return results.All(result => result.Status == BatchItemStatus.Succeeded)
            ? SuccessExitCode
            : ProcessingErrorExitCode;
    }

    private async Task<int> UnknownCommandAsync(string verb)
    {
        await _error.WriteLineAsync($"Unknown command '{verb}'. Run 'reelpress --help'.").ConfigureAwait(false);
        return UsageErrorExitCode;
    }

    private static TrimOperation BuildTrim(CommandArguments args)
    {
        var start = ParseTime(args.GetValue("start") ?? "00:00:00", "start");
        TimeSpan? end = args.GetValue("end") is { } endValue ? ParseTime(endValue, "end") : null;
        TimeSpan? duration = args.GetValue("duration") is { } durationValue ? ParseTime(durationValue, "duration") : null;
        if (end is null && duration is null)
        {
            throw new CliUsageException("trim requires --end <time> or --duration <time>.");
        }

        var mode = args.HasFlag("copy")
            ? TrimMode.ForceCopy
            : args.HasFlag("reencode")
                ? TrimMode.ForceReencode
                : TrimMode.AutoPreferCopy;
        return new TrimOperation(start, end, duration, mode);
    }

    private static ConvertOperation BuildConvert(CommandArguments args) => new(
        ParseEnum(args.GetValue("container") ?? "mp4", VideoContainer.Mp4, "container"),
        ParseEnum(args.GetValue("video-codec") ?? "h264", VideoCodec.H264, "video-codec"),
        ParseEnum(args.GetValue("audio-codec") ?? "aac", AudioCodec.Aac, "audio-codec"));

    private static CompressOperation BuildCompress(CommandArguments args)
    {
        var codec = ParseEnum(args.GetValue("video-codec") ?? "h264", VideoCodec.H264, "video-codec");
        var audioBitrate = ParseInt(args.GetValue("audio-bitrate") ?? "128", "audio-bitrate");
        if (args.GetValue("target-mb") is { } targetMbValue)
        {
            var targetMb = ParseInt(targetMbValue, "target-mb");
            return new CompressOperation(
                CompressionMode.TargetSize,
                targetSizeBytes: checked((long)targetMb * 1024 * 1024),
                audioBitrateKbps: audioBitrate,
                videoCodec: codec);
        }

        return new CompressOperation(
            CompressionMode.QualityCrf,
            crf: ParseInt(args.GetValue("crf") ?? "23", "crf"),
            audioBitrateKbps: audioBitrate,
            videoCodec: codec);
    }

    private static ResizeOperation BuildResize(CommandArguments args)
    {
        ResizePreset? preset = null;
        if (args.GetValue("preset") is { } presetValue)
        {
            preset = presetValue.Trim().ToLowerInvariant() switch
            {
                "1080p" or "p1080" => ResizePreset.P1080,
                "720p" or "p720" => ResizePreset.P720,
                "480p" or "p480" => ResizePreset.P480,
                _ => throw new CliUsageException($"Invalid preset '{presetValue}'. Use 1080p, 720p, or 480p.")
            };
        }

        var width = ParseOptionalInt(args.GetValue("width"), "width");
        var height = ParseOptionalInt(args.GetValue("height"), "height");
        if (preset is null && width is null && height is null)
        {
            throw new CliUsageException("resize requires --preset, --width, or --height.");
        }

        return new ResizeOperation(
            preset,
            width,
            height,
            ParseEnum(args.GetValue("mode") ?? "fit", ResizeMode.Fit, "mode"),
            args.HasFlag("allow-upscale"));
    }

    private static IVideoOperation BuildAudio(CommandArguments args)
    {
        if (args.HasFlag("mute"))
        {
            return new MuteOperation();
        }

        return new ExtractAudioOperation(
            ParseEnum(args.GetValue("format") ?? "mp3", AudioExtractionFormat.Mp3, "format"),
            ParseInt(args.GetValue("bitrate") ?? "192", "bitrate"));
    }

    private static ExtractFramesOperation BuildFrames(CommandArguments args)
    {
        TimeSpan? at = args.GetValue("at") is { } atValue ? ParseTime(atValue, "at") : null;
        TimeSpan? every = args.GetValue("every") is { } everyValue ? ParseTime(everyValue, "every") : null;
        if (at is null && every is null)
        {
            throw new CliUsageException("frames requires --at <time> or --every <interval>.");
        }

        return new ExtractFramesOperation(
            every,
            at,
            ParseEnum(args.GetValue("format") ?? "png", FrameImageFormat.Png, "format"),
            ParseInt(args.GetValue("quality") ?? "2", "quality"));
    }

    private static ExportAnimationOperation BuildAnimation(CommandArguments args)
    {
        var start = ParseTime(args.GetValue("start") ?? "00:00:00", "start");
        TimeSpan? end = args.GetValue("end") is { } endValue ? ParseTime(endValue, "end") : null;
        TimeSpan? duration = args.GetValue("duration") is { } durationValue
            ? ParseTime(durationValue, "duration")
            : end is null ? TimeSpan.FromSeconds(3) : null;
        return new ExportAnimationOperation(
            ParseEnum(args.GetValue("format") ?? "gif", AnimatedImageFormat.Gif, "format"),
            start,
            end,
            duration,
            ParseInt(args.GetValue("fps") ?? "15", "fps"),
            ParseInt(args.GetValue("width") ?? "480", "width"));
    }

    private async Task<MergeOperation> BuildMergeAsync(CommandArguments args, CancellationToken cancellationToken)
    {
        if (args.Positionals.Count < 2)
        {
            throw new CliUsageException("merge requires at least two input paths.");
        }

        var mediaInfos = await Task.WhenAll(
            args.Positionals.Select(path => _probe.ProbeAsync(path, cancellationToken))).ConfigureAwait(false);

        return new MergeOperation(
            args.Positionals,
            ParseEnum(args.GetValue("mode") ?? "auto", MergeMode.Auto, "mode"),
            mediaInfos);
    }

    private static IReadOnlyList<string> ResolveInputs(string sourcePath)
    {
        if (File.Exists(sourcePath))
        {
            return new[] { sourcePath };
        }

        if (!Directory.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Input file or folder does not exist: {sourcePath}", sourcePath);
        }

        return Directory.EnumerateFiles(sourcePath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedMediaExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveOutputFileName(string inputPath, IReadOnlyList<IVideoOperation> operations)
    {
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        var formatOperation = operations.LastOrDefault(operation => operation is
            ConvertOperation or ExtractAudioOperation or ExportAnimationOperation or ExtractFramesOperation);
        var suffix = formatOperation switch
        {
            ConvertOperation value => value.Container switch
            {
                VideoContainer.Mp4 => ".mp4",
                VideoContainer.Mkv => ".mkv",
                VideoContainer.Mov => ".mov",
                VideoContainer.WebM => ".webm",
                _ => Path.GetExtension(inputPath)
            },
            ExtractAudioOperation value => value.Format switch
            {
                AudioExtractionFormat.Mp3 => ".mp3",
                AudioExtractionFormat.Aac => ".aac",
                AudioExtractionFormat.Wav => ".wav",
                AudioExtractionFormat.Flac => ".flac",
                _ => ".audio"
            },
            ExportAnimationOperation value => value.Format == AnimatedImageFormat.Gif ? ".gif" : ".webp",
            ExtractFramesOperation value =>
                (value.EveryInterval is not null ? "-%06d" : string.Empty)
                + (value.Format == FrameImageFormat.Png ? ".png" : ".jpg"),
            _ => Path.GetExtension(inputPath)
        };

        return stem + suffix;
    }

    private static IReadOnlyList<string> ResolveOutputFileNames(
        IReadOnlyList<string> inputPaths,
        IReadOnlyList<IVideoOperation> operations)
    {
        var preferredNames = inputPaths.Select(path => ResolveOutputFileName(path, operations)).ToArray();
        var counts = preferredNames
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(
            preferredNames.Where(name => counts[name] == 1),
            StringComparer.OrdinalIgnoreCase);
        var resolvedNames = new string[preferredNames.Length];

        for (var index = 0; index < preferredNames.Length; index++)
        {
            var preferredName = preferredNames[index];
            if (counts[preferredName] == 1)
            {
                resolvedNames[index] = preferredName;
                continue;
            }

            var sourceExtension = Path.GetExtension(inputPaths[index]).TrimStart('.').ToLowerInvariant();
            var targetExtension = Path.GetExtension(preferredName);
            var targetStem = Path.GetFileNameWithoutExtension(preferredName);
            var baseName = $"{targetStem}-{sourceExtension}";
            var candidate = baseName + targetExtension;
            var suffix = 2;
            while (!usedNames.Add(candidate))
            {
                candidate = $"{baseName}-{suffix++}{targetExtension}";
            }

            resolvedNames[index] = candidate;
        }

        return resolvedNames;
    }

    private static void ValidateOptions(string verb, CommandArguments args)
    {
        if (!CommandOptions.TryGetValue(verb, out var supportedOptions))
        {
            return;
        }

        var unsupportedOption = args.OptionNames.FirstOrDefault(name => !supportedOptions.Contains(name));
        if (unsupportedOption is not null)
        {
            throw new CliUsageException($"Unsupported option '--{unsupportedOption}' for command '{verb}'.");
        }
    }

    private static T ParseEnum<T>(string value, T _, string option) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value.Replace("-", string.Empty, StringComparison.Ordinal), true, out var parsed))
        {
            return parsed;
        }

        throw new CliUsageException($"Invalid --{option} value '{value}'. Valid values: {string.Join(", ", Enum.GetNames<T>())}.");
    }

    private static int ParseInt(string value, string option)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new CliUsageException($"Invalid integer for --{option}: '{value}'.");
    }

    private static int? ParseOptionalInt(string? value, string option) =>
        value is null ? null : ParseInt(value, option);

    private static TimeSpan ParseTime(string value, string option)
    {
        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        throw new CliUsageException($"Invalid time for --{option}: '{value}'. Use HH:MM:SS or seconds.");
    }

    public const string HelpText = """
reelpress - local, scriptable video toolkit

Usage: reelpress <command> [arguments] [options]

Commands:
  probe     Inspect normalized media metadata
  run       Apply a JSON recipe to a file or folder
  trim      Trim a clip
  convert   Convert container/codecs
  compress  Compress by CRF or target size
  resize    Resize a video
  merge     Concatenate two or more videos
  audio     Extract audio or mute a video
  frames    Extract one or more frames
  gif       Export an optimized GIF or animated WebP

Run 'reelpress <command> --help' for command flags. Global output flag: --json.
Exit codes: 0 success, 1 processing failure, 2 invalid command or arguments.
""";

    private static readonly IReadOnlyDictionary<string, string> CommandHelp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["probe"] = "Usage: reelpress probe <input> [--json]",
        ["run"] = "Usage: reelpress run --recipe <json-or-preset> <file-or-folder> --out <folder> [--json]",
        ["trim"] = "Usage: reelpress trim <input> --start <time> (--end <time>|--duration <time>) [--copy|--reencode] --out <file> [--json]",
        ["convert"] = "Usage: reelpress convert <input> [--container mp4|mkv|mov|webm] [--video-codec h264|h265|vp9|av1|copy] [--audio-codec aac|opus|mp3|copy|none] --out <file> [--json]",
        ["compress"] = "Usage: reelpress compress <input> [--crf 23|--target-mb 20] [--video-codec h264|h265|vp9|av1] [--audio-bitrate 128] --out <file> [--json]",
        ["resize"] = "Usage: reelpress resize <input> [--preset 1080p|720p|480p|--width N --height N] [--mode fit|pad|crop|stretch] [--allow-upscale] --out <file> [--json]",
        ["merge"] = "Usage: reelpress merge <input1> <input2> [...] [--mode auto|forceConcatDemuxer|forceReencode] --out <file> [--json]",
        ["audio"] = "Usage: reelpress audio [extract] <input> [--format mp3|aac|wav|flac] [--bitrate 192] [--mute] --out <file> [--json]",
        ["frames"] = "Usage: reelpress frames <input> (--at <time>|--every <interval>) [--format png|jpeg] [--quality 2] --out <file-or-pattern> [--json]",
        ["gif"] = "Usage: reelpress gif <input> [--format gif|webp] [--start <time>] [--end <time>|--duration <time>] [--fps 15] [--width 480] --out <file> [--json]"
    };

    private static readonly IReadOnlyDictionary<string, HashSet<string>> CommandOptions =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["probe"] = Options("json"),
            ["run"] = Options("recipe", "out", "json"),
            ["trim"] = Options("start", "end", "duration", "copy", "reencode", "out", "json"),
            ["convert"] = Options("container", "video-codec", "audio-codec", "out", "json"),
            ["compress"] = Options("crf", "target-mb", "video-codec", "audio-bitrate", "out", "json"),
            ["resize"] = Options("preset", "width", "height", "mode", "allow-upscale", "out", "json"),
            ["merge"] = Options("mode", "out", "json"),
            ["audio"] = Options("format", "bitrate", "mute", "out", "json"),
            ["frames"] = Options("at", "every", "format", "quality", "out", "json"),
            ["gif"] = Options("format", "start", "end", "duration", "fps", "width", "out", "json")
        };

    private static HashSet<string> Options(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);

    private sealed class CliUsageException(string message) : Exception(message);

    private sealed class CommandArguments
    {
        private static readonly HashSet<string> FlagNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "json", "copy", "reencode", "allow-upscale", "mute"
        };

        private CommandArguments(List<string> positionals, Dictionary<string, string?> options)
        {
            Positionals = positionals;
            Options = options;
        }

        public IReadOnlyList<string> Positionals { get; }
        public IEnumerable<string> OptionNames => Options.Keys;
        private IReadOnlyDictionary<string, string?> Options { get; }

        public static CommandArguments Parse(IEnumerable<string> values)
        {
            var tokens = values.ToArray();
            var positionals = new List<string>();
            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                {
                    if (token.StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new CliUsageException($"Unsupported option '{token}'.");
                    }

                    positionals.Add(token);
                    continue;
                }

                var name = token[2..];
                if (FlagNames.Contains(name))
                {
                    options[name] = null;
                    continue;
                }

                if (i + 1 >= tokens.Length || tokens[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new CliUsageException($"Option --{name} requires a value.");
                }

                options[name] = tokens[++i];
            }

            return new CommandArguments(positionals, options);
        }

        public bool HasFlag(string name) => Options.ContainsKey(name);
        public string? GetValue(string name) => Options.TryGetValue(name, out var value) ? value : null;
        public string RequireValue(string name, string message) => GetValue(name) ?? throw new CliUsageException(message);
        public string RequirePositional(int index, string message) => index < Positionals.Count ? Positionals[index] : throw new CliUsageException(message);
        public CommandArguments WithoutFirstPositional() => new(Positionals.Skip(1).ToList(), new Dictionary<string, string?>(Options, StringComparer.OrdinalIgnoreCase));
    }
}
