namespace ReelPress.Core.Tests;

public sealed class JsonRecipeStoreTests
{
    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsRecipeOperations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"reelpress-recipes-{Guid.NewGuid():N}");
        var store = new JsonRecipeStore(root);
        var recipe = new Recipe(
            Name: "social clip",
            Operations:
            [
                new TrimOperation(TimeSpan.FromSeconds(2), end: TimeSpan.FromSeconds(8), mode: TrimMode.ForceReencode),
                new ResizeOperation(width: 720, height: 720, mode: ResizeMode.Crop, allowUpscale: true),
                new CompressOperation(CompressionMode.QualityCrf, crf: 25, videoCodec: VideoCodec.H265),
                new ConvertOperation(VideoContainer.Mp4, VideoCodec.H265, AudioCodec.Aac)
            ]);

        try
        {
            await store.SaveAsync("social", recipe);
            var loaded = await store.LoadAsync("social");

            Assert.Equal(recipe.Name, loaded.Name);
            Assert.Collection(
                loaded.Operations,
                operation =>
                {
                    var trim = Assert.IsType<TrimOperation>(operation);
                    Assert.Equal(TimeSpan.FromSeconds(2), trim.Start);
                    Assert.Equal(TimeSpan.FromSeconds(8), trim.End);
                    Assert.Equal(TrimMode.ForceReencode, trim.Mode);
                },
                operation =>
                {
                    var resize = Assert.IsType<ResizeOperation>(operation);
                    Assert.Equal(720, resize.Width);
                    Assert.Equal(720, resize.Height);
                    Assert.Equal(ResizeMode.Crop, resize.Mode);
                    Assert.True(resize.AllowUpscale);
                },
                operation =>
                {
                    var compress = Assert.IsType<CompressOperation>(operation);
                    Assert.Equal(25, compress.Crf);
                    Assert.Equal(VideoCodec.H265, compress.VideoCodec);
                },
                operation =>
                {
                    var convert = Assert.IsType<ConvertOperation>(operation);
                    Assert.Equal(VideoContainer.Mp4, convert.Container);
                    Assert.Equal(VideoCodec.H265, convert.VideoCodec);
                    Assert.Equal(AudioCodec.Aac, convert.AudioCodec);
                });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
