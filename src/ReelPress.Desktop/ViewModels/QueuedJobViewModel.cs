using ReelPress.Core;

namespace ReelPress.Desktop.ViewModels;

public sealed class QueuedJobViewModel : ViewModelBase
{
    private string _outputPath = string.Empty;
    private BatchItemStatus _status = BatchItemStatus.Queued;
    private double? _progressPercent;
    private string _message = "Queued";

    public QueuedJobViewModel(string inputPath)
    {
        InputPath = inputPath;
    }

    public string InputPath { get; }

    public string FileName => Path.GetFileName(InputPath);

    public string OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public BatchItemStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public double? ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }
}
