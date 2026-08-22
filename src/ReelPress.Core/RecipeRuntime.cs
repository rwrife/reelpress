namespace ReelPress.Core;

public static class RecipeRuntime
{
    public static async Task<IReadOnlyList<IVideoOperation>> PrepareOperationsAsync(
        IEnumerable<IVideoOperation> operations,
        IMediaProbe mediaProbe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(mediaProbe);

        var prepared = new List<IVideoOperation>();
        foreach (var operation in operations)
        {
            if (operation is not MergeOperation merge)
            {
                prepared.Add(operation);
                continue;
            }

            var mediaInfos = await Task.WhenAll(
                merge.InputPaths.Select(path => mediaProbe.ProbeAsync(path, cancellationToken)))
                .ConfigureAwait(false);
            prepared.Add(new MergeOperation(merge.InputPaths, merge.Mode, mediaInfos));
        }

        return prepared;
    }
}
