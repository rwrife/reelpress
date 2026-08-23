using System.Globalization;
using ReelPress.Core;

namespace ReelPress.Desktop.ViewModels;

public static class RecipePipelineMapper
{
    public static IReadOnlyList<PipelineStepViewModel> ToViewModels(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe.Operations.Select(ToViewModel).ToArray();
    }

    public static Recipe ToRecipe(string name, IEnumerable<PipelineStepViewModel> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return new Recipe(name, steps.Select(step => step.BuildOperation()).ToArray());
    }

    private static PipelineStepViewModel ToViewModel(IVideoOperation operation) => operation switch
    {
        ConvertOperation value => new ConvertStepViewModel
        {
            Container = value.Container,
            VideoCodec = value.VideoCodec,
            AudioCodec = value.AudioCodec
        },
        ResizeOperation { Preset: not null, Width: null, Height: null } value => new ResizeStepViewModel
        {
            Preset = value.Preset.Value,
            Mode = value.Mode,
            AllowUpscale = value.AllowUpscale
        },
        CompressOperation { Mode: CompressionMode.QualityCrf } value => new CompressStepViewModel
        {
            Mode = value.Mode,
            Crf = value.Crf,
            AudioBitrateKbps = value.AudioBitrateKbps,
            VideoCodec = value.VideoCodec
        },
        CompressOperation { Mode: CompressionMode.TargetSize } value
            when value.TargetSizeBytes > 0 && value.TargetSizeBytes % (1024 * 1024) == 0 && value.TargetSizeBytes / (1024 * 1024) <= int.MaxValue =>
            new CompressStepViewModel
            {
                Mode = value.Mode,
                TargetSizeMb = (int)(value.TargetSizeBytes / (1024 * 1024)),
                AudioBitrateKbps = value.AudioBitrateKbps,
                VideoCodec = value.VideoCodec
            },
        TrimOperation { Duration: null, End: not null } value => new TrimStepViewModel
        {
            Start = FormatTime(value.Start),
            End = FormatTime(value.End.Value),
            Mode = value.Mode
        },
        _ => new RecipeOperationStepViewModel(operation)
    };

    private static string FormatTime(TimeSpan value) =>
        value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}

public sealed class RecipeOperationStepViewModel : PipelineStepViewModel
{
    private readonly IVideoOperation _operation;

    public RecipeOperationStepViewModel(IVideoOperation operation)
    {
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
    }

    public override string DisplayName => _operation.Name;

    public override string Description => "Loaded from recipe (run/save supported; edit in JSON or CLI).";

    public override IVideoOperation BuildOperation() => _operation;
}
