namespace ReelPress.Core;

public interface IMediaProbe
{
    Task<MediaInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken = default);

    MediaInfo ParseProbeJson(string ffprobeJson);
}
