using ReelPress.Core;

namespace ReelPress.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var engine = new FfmpegEngine();
            var probe = new MediaProbe(engine);
            var app = new CliApplication(engine, probe, new JsonRecipeStore(), Console.Out, Console.Error);
            return await app.RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return CliApplication.ProcessingErrorExitCode;
        }
    }
}
