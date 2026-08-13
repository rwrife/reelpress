using System.Runtime.InteropServices;

namespace ReelPress.Core;

public sealed class FfmpegBinaryResolver
{
    private readonly FfmpegEngineOptions _options;

    public FfmpegBinaryResolver(FfmpegEngineOptions? options = null)
    {
        _options = options ?? new FfmpegEngineOptions();
    }

    public string ResolveFfmpegPath() => ResolveBinary(_options.FfmpegPathOverride, "ffmpeg");

    public string ResolveFfprobePath() => ResolveBinary(_options.FfprobePathOverride, "ffprobe");

    private string ResolveBinary(string? overridePath, string binaryBaseName)
    {
        var binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{binaryBaseName}.exe"
            : binaryBaseName;

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(overridePath);
            var fullPath = Path.GetFullPath(expanded);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            throw new FileNotFoundException(
                $"Configured {binaryBaseName} path does not exist: {fullPath}",
                fullPath);
        }

        var rid = GetRuntimeIdentifier();
        var searched = new List<string>();

        foreach (var root in GetCandidateRoots())
        {
            foreach (var candidate in BuildBundledCandidates(root, rid, binaryName))
            {
                searched.Add(candidate);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var fromPath = TryResolveFromPath(binaryName);
        if (fromPath is not null)
        {
            return fromPath;
        }

        var searchedText = searched.Count == 0
            ? "(no bundled roots discovered)"
            : string.Join(Environment.NewLine, searched.Select(path => $" - {path}"));

        throw new FileNotFoundException(
            $"Unable to resolve '{binaryBaseName}' for runtime '{rid}'. " +
            "Install FFmpeg or bundle platform binaries under runtimes/<rid>/native/. " +
            "Checked paths:" + Environment.NewLine + searchedText);
    }

    private static IEnumerable<string> GetCandidateRoots()
    {
        var roots = new List<string>();

        var appBase = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(appBase))
        {
            roots.Add(Path.GetFullPath(appBase));
        }

        var current = Environment.CurrentDirectory;
        if (!string.IsNullOrWhiteSpace(current))
        {
            roots.Add(Path.GetFullPath(current));
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> BuildBundledCandidates(string root, string rid, string binaryName)
    {
        var candidates = new List<string>
        {
            Path.Combine(root, "runtimes", rid, "native", binaryName),
            Path.Combine(root, "tools", rid, binaryName),
            Path.Combine(root, "binaries", rid, binaryName)
        };

        if (!string.IsNullOrWhiteSpace(_options.BundleRootPath))
        {
            var bundleRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_options.BundleRootPath));
            candidates.Insert(0, Path.Combine(bundleRoot, "runtimes", rid, "native", binaryName));
            candidates.Insert(1, Path.Combine(bundleRoot, binaryName));
        }

        return candidates;
    }

    private static string? TryResolveFromPath(string binaryName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(segment.Trim(), binaryName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "win-x64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "osx-arm64"
                : "osx-x64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "linux-arm64"
                : "linux-x64";
        }

        return $"unknown-{RuntimeInformation.ProcessArchitecture}";
    }
}
