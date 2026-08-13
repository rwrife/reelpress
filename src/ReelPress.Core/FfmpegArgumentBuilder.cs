namespace ReelPress.Core;

public static class FfmpegArgumentBuilder
{
    public static IReadOnlyList<string> Build(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.ToArray();
    }

    public static IReadOnlyList<string> Build(params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.ToArray();
    }
}
