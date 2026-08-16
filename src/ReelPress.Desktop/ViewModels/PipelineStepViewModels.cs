using System.Globalization;
using ReelPress.Core;

namespace ReelPress.Desktop.ViewModels;

public abstract class PipelineStepViewModel : ViewModelBase
{
    public abstract string DisplayName { get; }

    public abstract string Description { get; }

    public abstract IVideoOperation BuildOperation();
}

public sealed class ResizeStepViewModel : PipelineStepViewModel
{
    private ResizePreset _preset = ResizePreset.P720;
    private ResizeMode _mode = ResizeMode.Fit;
    private bool _allowUpscale;

    public override string DisplayName => "Resize";

    public override string Description => $"Preset: {_preset}, mode: {_mode}";

    public ResizePreset Preset
    {
        get => _preset;
        set
        {
            if (SetProperty(ref _preset, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public ResizeMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public bool AllowUpscale
    {
        get => _allowUpscale;
        set => SetProperty(ref _allowUpscale, value);
    }

    public override IVideoOperation BuildOperation() =>
        new ResizeOperation(preset: Preset, mode: Mode, allowUpscale: AllowUpscale);
}

public sealed class CompressStepViewModel : PipelineStepViewModel
{
    private CompressionMode _mode = CompressionMode.QualityCrf;
    private int _crf = 23;
    private int _targetSizeMb = 25;
    private int _audioBitrateKbps = 128;
    private VideoCodec _videoCodec = VideoCodec.H264;

    public override string DisplayName => "Compress";

    public override string Description =>
        Mode == CompressionMode.QualityCrf
            ? $"CRF {_crf} ({_videoCodec})"
            : $"~{_targetSizeMb} MB ({_videoCodec})";

    public CompressionMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public int Crf
    {
        get => _crf;
        set
        {
            if (SetProperty(ref _crf, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public int TargetSizeMb
    {
        get => _targetSizeMb;
        set
        {
            if (SetProperty(ref _targetSizeMb, Math.Max(1, value)))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public int AudioBitrateKbps
    {
        get => _audioBitrateKbps;
        set => SetProperty(ref _audioBitrateKbps, Math.Max(32, value));
    }

    public VideoCodec VideoCodec
    {
        get => _videoCodec;
        set
        {
            if (SetProperty(ref _videoCodec, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public override IVideoOperation BuildOperation()
    {
        var targetBytes = (long)TargetSizeMb * 1024 * 1024;

        return new CompressOperation(
            mode: Mode,
            crf: Crf,
            targetSizeBytes: targetBytes,
            audioBitrateKbps: AudioBitrateKbps,
            videoCodec: VideoCodec);
    }
}

public sealed class ConvertStepViewModel : PipelineStepViewModel
{
    private VideoContainer _container = VideoContainer.Mp4;
    private VideoCodec _videoCodec = VideoCodec.H264;
    private AudioCodec _audioCodec = AudioCodec.Aac;

    public override string DisplayName => "Convert";

    public override string Description => $"{_container} · {_videoCodec}/{_audioCodec}";

    public VideoContainer Container
    {
        get => _container;
        set
        {
            if (SetProperty(ref _container, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public VideoCodec VideoCodec
    {
        get => _videoCodec;
        set
        {
            if (SetProperty(ref _videoCodec, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public AudioCodec AudioCodec
    {
        get => _audioCodec;
        set
        {
            if (SetProperty(ref _audioCodec, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public override IVideoOperation BuildOperation() =>
        new ConvertOperation(Container, VideoCodec, AudioCodec);
}

public sealed class TrimStepViewModel : PipelineStepViewModel
{
    private string _start = "00:00:00";
    private string _end = "00:00:10";
    private TrimMode _mode = TrimMode.AutoPreferCopy;

    public override string DisplayName => "Trim";

    public override string Description => $"{_start} → {_end} ({_mode})";

    public string Start
    {
        get => _start;
        set
        {
            if (SetProperty(ref _start, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public string End
    {
        get => _end;
        set
        {
            if (SetProperty(ref _end, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public TrimMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public override IVideoOperation BuildOperation()
    {
        var start = ParseTimeOrDefault(Start, TimeSpan.Zero);
        var end = ParseTimeOrDefault(End, start + TimeSpan.FromSeconds(10));

        return new TrimOperation(start, end: end, mode: Mode);
    }

    private static TimeSpan ParseTimeOrDefault(string raw, TimeSpan fallback)
    {
        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed) && parsed >= TimeSpan.Zero)
        {
            return parsed;
        }

        return fallback;
    }
}
