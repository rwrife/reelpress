namespace ReelPress.Core;

public static class VideoOperationPlanner
{
    public static IReadOnlyList<string> BuildArguments(
        MediaInfo mediaInfo,
        string inputPath,
        string outputPath,
        params IVideoOperation[] operations)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(operations);

        var validationErrors = Validate(mediaInfo, operations);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot build ffmpeg args due to invalid operation config:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", validationErrors)}");
        }

        var context = new VideoOperationContext();
        foreach (var operation in operations)
        {
            operation.Apply(mediaInfo, context);
        }

        return context.BuildArguments(inputPath, outputPath);
    }

    public static IReadOnlyList<string> Validate(MediaInfo mediaInfo, IEnumerable<IVideoOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(operations);

        var errors = new List<string>();

        foreach (var operation in operations)
        {
            if (operation is null)
            {
                errors.Add("Operation cannot be null.");
                continue;
            }

            var operationErrors = operation.Validate(mediaInfo);
            foreach (var error in operationErrors)
            {
                errors.Add($"{operation.Name}: {error}");
            }
        }

        return errors;
    }
}
