using System.Globalization;

namespace ReelPress.Core;

internal static class FfmpegValueFormatter
{
    public static string FormatTime(TimeSpan value)
    {
        var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return clamped.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    public static string FormatKbps(long bitsPerSecond)
    {
        var kbps = Math.Max(1, bitsPerSecond / 1000d);
        return $"{Math.Round(kbps, MidpointRounding.AwayFromZero):0}k";
    }
}
