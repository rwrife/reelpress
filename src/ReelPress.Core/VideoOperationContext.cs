namespace ReelPress.Core;

public sealed class VideoOperationContext
{
    private readonly List<string> _preInputArguments = new();
    private readonly List<string> _postInputArguments = new();
    private readonly List<string> _videoFilters = new();

    public IReadOnlyList<string> PreInputArguments => _preInputArguments;

    public IReadOnlyList<string> PostInputArguments => _postInputArguments;

    public IReadOnlyList<string> VideoFilters => _videoFilters;

    public void AddPreInputArguments(params string[] arguments)
    {
        AddArguments(_preInputArguments, arguments);
    }

    public void AddPostInputArguments(params string[] arguments)
    {
        AddArguments(_postInputArguments, arguments);
    }

    public void AddVideoFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            throw new ArgumentException("Video filter is required.", nameof(filter));
        }

        _videoFilters.Add(filter);
    }

    public IReadOnlyList<string> BuildArguments(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var args = new List<string>();
        args.AddRange(_preInputArguments);
        args.Add("-i");
        args.Add(inputPath);
        args.AddRange(_postInputArguments);

        if (_videoFilters.Count > 0)
        {
            args.Add("-vf");
            args.Add(string.Join(',', _videoFilters));
        }

        args.Add(outputPath);
        return args;
    }

    private static void AddArguments(List<string> destination, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Length == 0)
        {
            return;
        }

        foreach (var argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                throw new ArgumentException("Arguments cannot contain null or whitespace values.", nameof(arguments));
            }

            destination.Add(argument);
        }
    }
}
