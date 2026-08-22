using ReelPress.Core;
using ReelPress.Desktop.ViewModels;

namespace ReelPress.Desktop.Tests;

public sealed class RecipePipelineMapperTests
{
    [Fact]
    public void CliRecipe_RoundTripsThroughDesktopPipeline()
    {
        var recipe = new Recipe(
            "podcast",
            [
                new ConvertOperation(VideoContainer.WebM, VideoCodec.VP9, AudioCodec.Opus),
                new ExtractAudioOperation(AudioExtractionFormat.Flac)
            ]);

        var viewModels = RecipePipelineMapper.ToViewModels(recipe);
        var roundTripped = RecipePipelineMapper.ToRecipe(recipe.Name, viewModels);

        var convert = Assert.IsType<ConvertOperation>(roundTripped.Operations[0]);
        Assert.Equal(VideoContainer.WebM, convert.Container);
        Assert.Equal(VideoCodec.VP9, convert.VideoCodec);
        Assert.Equal(AudioCodec.Opus, convert.AudioCodec);
        var audio = Assert.IsType<ExtractAudioOperation>(roundTripped.Operations[1]);
        Assert.Equal(AudioExtractionFormat.Flac, audio.Format);
    }
}
