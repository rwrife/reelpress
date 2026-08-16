using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ReelPress.Core;

namespace ReelPress.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".webm", ".avi", ".m4v"
    };

    private readonly IFfmpegEngine _ffmpegEngine;
    private readonly IMediaProbe _mediaProbe;
    private readonly PipelineRunner _pipelineRunner;

    private CancellationTokenSource? _runCts;
    private QueuedJobViewModel? _selectedJob;
    private PipelineStepViewModel? _selectedStep;
    private string _newStepType = "Resize";
    private string _statusMessage = "Drop video files or folders to build a batch.";
    private string _resultSummary = string.Empty;
    private bool _isRunning;
    private bool _writeInPlace = true;
    private string _outputDirectory = string.Empty;
    private int _maxConcurrency = 2;
    private double _overallProgress;
    private string _estimateText = "Select a file to see preview + estimated output size.";
    private Uri? _beforePreviewUri;
    private Uri? _afterPreviewUri;

    public MainWindowViewModel()
    {
        try
        {
            _ffmpegEngine = new FfmpegEngine();
        }
        catch
        {
            // Fall back to path-only constructor so the shell can still launch.
            _ffmpegEngine = new FfmpegEngine("ffmpeg", "ffprobe");
        }

        _mediaProbe = new MediaProbe(_ffmpegEngine);
        _pipelineRunner = new PipelineRunner(_ffmpegEngine, _mediaProbe);

        AddPathCommand = new RelayCommand<string?>(AddPathFromText);
        RemoveSelectedJobCommand = new RelayCommand(RemoveSelectedJob);
        AddStepCommand = new RelayCommand(AddSelectedStep);
        RemoveSelectedStepCommand = new RelayCommand(RemoveSelectedStep);
        MoveStepUpCommand = new RelayCommand(MoveStepUp);
        MoveStepDownCommand = new RelayCommand(MoveStepDown);
        RunQueueCommand = new AsyncRelayCommand(RunQueueAsync);
        CancelRunCommand = new RelayCommand(CancelRun);

        // Starter defaults for common "resize + compress + convert" workflows.
        PipelineSteps.Add(new ResizeStepViewModel());
        PipelineSteps.Add(new CompressStepViewModel());
        PipelineSteps.Add(new ConvertStepViewModel());
        SelectedStep = PipelineSteps[0];
    }

    public ObservableCollection<QueuedJobViewModel> Jobs { get; } = new();

    public ObservableCollection<PipelineStepViewModel> PipelineSteps { get; } = new();

    public IReadOnlyList<string> StepTypeOptions { get; } =
    [
        "Resize",
        "Compress",
        "Convert",
        "Trim"
    ];

    public IReadOnlyList<ResizePreset> ResizePresetOptions { get; } = Enum.GetValues<ResizePreset>();

    public IReadOnlyList<ResizeMode> ResizeModeOptions { get; } = Enum.GetValues<ResizeMode>();

    public IReadOnlyList<CompressionMode> CompressionModeOptions { get; } = Enum.GetValues<CompressionMode>();

    public IReadOnlyList<VideoCodec> VideoCodecOptions { get; } = Enum.GetValues<VideoCodec>();

    public IReadOnlyList<VideoContainer> VideoContainerOptions { get; } = Enum.GetValues<VideoContainer>();

    public IReadOnlyList<AudioCodec> AudioCodecOptions { get; } = Enum.GetValues<AudioCodec>();

    public IReadOnlyList<TrimMode> TrimModeOptions { get; } = Enum.GetValues<TrimMode>();

    public IRelayCommand<string?> AddPathCommand { get; }

    public IRelayCommand RemoveSelectedJobCommand { get; }

    public IRelayCommand AddStepCommand { get; }

    public IRelayCommand RemoveSelectedStepCommand { get; }

    public IRelayCommand MoveStepUpCommand { get; }

    public IRelayCommand MoveStepDownCommand { get; }

    public IAsyncRelayCommand RunQueueCommand { get; }

    public IRelayCommand CancelRunCommand { get; }

    public QueuedJobViewModel? SelectedJob
    {
        get => _selectedJob;
        set
        {
            if (SetProperty(ref _selectedJob, value))
            {
                _ = RefreshSelectedJobDetailsAsync();
            }
        }
    }

    public PipelineStepViewModel? SelectedStep
    {
        get => _selectedStep;
        set => SetProperty(ref _selectedStep, value);
    }

    public string NewStepType
    {
        get => _newStepType;
        set => SetProperty(ref _newStepType, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ResultSummary
    {
        get => _resultSummary;
        set => SetProperty(ref _resultSummary, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    public bool WriteInPlace
    {
        get => _writeInPlace;
        set => SetProperty(ref _writeInPlace, value);
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set => SetProperty(ref _outputDirectory, value);
    }

    public int MaxConcurrency
    {
        get => _maxConcurrency;
        set => SetProperty(ref _maxConcurrency, Math.Max(1, value));
    }

    public double OverallProgress
    {
        get => _overallProgress;
        set => SetProperty(ref _overallProgress, value);
    }

    public string EstimateText
    {
        get => _estimateText;
        set => SetProperty(ref _estimateText, value);
    }

    public Uri? BeforePreviewUri
    {
        get => _beforePreviewUri;
        set => SetProperty(ref _beforePreviewUri, value);
    }

    public Uri? AfterPreviewUri
    {
        get => _afterPreviewUri;
        set => SetProperty(ref _afterPreviewUri, value);
    }

    public void AddPaths(IEnumerable<string> inputPaths)
    {
        var added = 0;

        foreach (var inputPath in inputPaths)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                continue;
            }

            if (Directory.Exists(inputPath))
            {
                var files = Directory
                    .EnumerateFiles(inputPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => SupportedVideoExtensions.Contains(Path.GetExtension(path)));

                foreach (var file in files)
                {
                    if (TryAddSinglePath(file))
                    {
                        added++;
                    }
                }

                continue;
            }

            if (TryAddSinglePath(inputPath))
            {
                added++;
            }
        }

        if (added > 0)
        {
            StatusMessage = $"Added {added} item(s) to queue.";
            SelectedJob ??= Jobs[0];
            return;
        }

        StatusMessage = "No supported video files were added.";
    }

    private bool TryAddSinglePath(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (!SupportedVideoExtensions.Contains(Path.GetExtension(path)))
        {
            return false;
        }

        if (Jobs.Any(job => string.Equals(job.InputPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        Jobs.Add(new QueuedJobViewModel(path));
        return true;
    }

    private void AddPathFromText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            StatusMessage = "Enter a file/folder path first.";
            return;
        }

        AddPaths(new[] { input.Trim() });
    }

    private void RemoveSelectedJob()
    {
        if (SelectedJob is null)
        {
            return;
        }

        var index = Jobs.IndexOf(SelectedJob);
        Jobs.Remove(SelectedJob);

        if (Jobs.Count == 0)
        {
            SelectedJob = null;
            return;
        }

        var nextIndex = Math.Clamp(index, 0, Jobs.Count - 1);
        SelectedJob = Jobs[nextIndex];
    }

    private void AddSelectedStep()
    {
        PipelineStepViewModel step = NewStepType switch
        {
            "Resize" => new ResizeStepViewModel(),
            "Compress" => new CompressStepViewModel(),
            "Convert" => new ConvertStepViewModel(),
            "Trim" => new TrimStepViewModel(),
            _ => new ResizeStepViewModel()
        };

        PipelineSteps.Add(step);
        SelectedStep = step;
        _ = RefreshSelectedJobDetailsAsync();
    }

    private void RemoveSelectedStep()
    {
        if (SelectedStep is null)
        {
            return;
        }

        var index = PipelineSteps.IndexOf(SelectedStep);
        PipelineSteps.Remove(SelectedStep);

        if (PipelineSteps.Count == 0)
        {
            SelectedStep = null;
            return;
        }

        var nextIndex = Math.Clamp(index, 0, PipelineSteps.Count - 1);
        SelectedStep = PipelineSteps[nextIndex];
        _ = RefreshSelectedJobDetailsAsync();
    }

    private void MoveStepUp()
    {
        if (SelectedStep is null)
        {
            return;
        }

        var index = PipelineSteps.IndexOf(SelectedStep);
        if (index <= 0)
        {
            return;
        }

        PipelineSteps.Move(index, index - 1);
        _ = RefreshSelectedJobDetailsAsync();
    }

    private void MoveStepDown()
    {
        if (SelectedStep is null)
        {
            return;
        }

        var index = PipelineSteps.IndexOf(SelectedStep);
        if (index < 0 || index >= PipelineSteps.Count - 1)
        {
            return;
        }

        PipelineSteps.Move(index, index + 1);
        _ = RefreshSelectedJobDetailsAsync();
    }

    private async Task RunQueueAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (Jobs.Count == 0)
        {
            StatusMessage = "Queue is empty.";
            return;
        }

        if (PipelineSteps.Count == 0)
        {
            StatusMessage = "Add at least one pipeline step before running.";
            return;
        }

        IsRunning = true;
        ResultSummary = string.Empty;
        OverallProgress = 0;
        _runCts = new CancellationTokenSource();

        try
        {
            var operations = BuildOperations();
            var batchJobs = Jobs
                .Select(job => new BatchJob(
                    InputPath: job.InputPath,
                    OutputPath: ResolveOutputPath(job.InputPath, operations),
                    Operations: operations,
                    DisplayName: job.FileName))
                .ToArray();

            var progress = new Progress<BatchJobProgress>(update =>
            {
                if (update.JobIndex < 0 || update.JobIndex >= Jobs.Count)
                {
                    return;
                }

                var jobVm = Jobs[update.JobIndex];
                jobVm.Status = update.Status;
                jobVm.Message = update.Message ?? update.Status.ToString();
                jobVm.ProgressPercent = update.Percent;

                if (update.Percent is not null)
                {
                    var total = Jobs.Sum(job => Math.Max(0, job.ProgressPercent ?? 0));
                    OverallProgress = total / Math.Max(1, Jobs.Count);
                }
            });

            var results = await _pipelineRunner.RunAsync(
                batchJobs,
                maxConcurrency: MaxConcurrency,
                progress: progress,
                cancellationToken: _runCts.Token);

            var successCount = 0;
            var skipCount = 0;
            var failCount = 0;

            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                var jobVm = Jobs[i];

                jobVm.OutputPath = result.OutputPath;
                jobVm.Status = result.Status;
                jobVm.Message = result.Message ?? result.Status.ToString();
                jobVm.ProgressPercent = result.Status == BatchItemStatus.Succeeded ? 100 : jobVm.ProgressPercent;

                switch (result.Status)
                {
                    case BatchItemStatus.Succeeded:
                        successCount++;
                        break;
                    case BatchItemStatus.Skipped:
                        skipCount++;
                        break;
                    case BatchItemStatus.Failed:
                    case BatchItemStatus.Canceled:
                        failCount++;
                        break;
                }
            }

            OverallProgress = 100;
            ResultSummary = $"Done. Success: {successCount}, Skipped: {skipCount}, Failed/Canceled: {failCount}.";
            StatusMessage = ResultSummary;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Batch canceled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Batch failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }

        await RefreshSelectedJobDetailsAsync();
    }

    private void CancelRun()
    {
        _runCts?.Cancel();
    }

    private IReadOnlyList<IVideoOperation> BuildOperations() =>
        PipelineSteps.Select(step => step.BuildOperation()).ToArray();

    private string ResolveOutputPath(string inputPath, IReadOnlyList<IVideoOperation> operations)
    {
        var inputDirectory = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
        var targetDirectory = WriteInPlace || string.IsNullOrWhiteSpace(OutputDirectory)
            ? inputDirectory
            : OutputDirectory;

        Directory.CreateDirectory(targetDirectory);

        var inputBaseName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = ResolveOutputExtension(inputPath, operations);

        return Path.Combine(targetDirectory, $"{inputBaseName}-reelpress{extension}");
    }

    private static string ResolveOutputExtension(string inputPath, IReadOnlyList<IVideoOperation> operations)
    {
        var last = operations.LastOrDefault();

        return last switch
        {
            ConvertOperation convert => convert.Container switch
            {
                VideoContainer.Mp4 => ".mp4",
                VideoContainer.Mkv => ".mkv",
                VideoContainer.Mov => ".mov",
                VideoContainer.WebM => ".webm",
                _ => Path.GetExtension(inputPath)
            },
            ExtractAudioOperation extractAudio => extractAudio.Format switch
            {
                AudioExtractionFormat.Mp3 => ".mp3",
                AudioExtractionFormat.Aac => ".aac",
                AudioExtractionFormat.Wav => ".wav",
                AudioExtractionFormat.Flac => ".flac",
                _ => ".audio"
            },
            ExportAnimationOperation animation => animation.Format == AnimatedImageFormat.Gif ? ".gif" : ".webp",
            _ => Path.GetExtension(inputPath)
        };
    }

    private async Task RefreshSelectedJobDetailsAsync()
    {
        var selected = SelectedJob;
        if (selected is null || !File.Exists(selected.InputPath))
        {
            BeforePreviewUri = null;
            AfterPreviewUri = null;
            EstimateText = "Select a local file to show preview and estimate.";
            return;
        }

        try
        {
            var mediaInfo = await _mediaProbe.ProbeAsync(selected.InputPath).ConfigureAwait(true);
            var operations = BuildOperations();

            var estimate = PipelineEstimator.Estimate(mediaInfo, operations);
            EstimateText = estimate.Summary;

            var midpoint = mediaInfo.Duration > TimeSpan.Zero
                ? TimeSpan.FromSeconds(mediaInfo.Duration.TotalSeconds / 2d)
                : TimeSpan.FromSeconds(1);

            var before = await CreatePreviewFrameAsync(selected.InputPath, midpoint, videoFilters: null).ConfigureAwait(true);
            var filters = BuildPreviewFilters(mediaInfo, operations);
            var after = await CreatePreviewFrameAsync(selected.InputPath, midpoint, filters).ConfigureAwait(true);

            BeforePreviewUri = before;
            AfterPreviewUri = after ?? before;
        }
        catch (Exception ex)
        {
            EstimateText = $"Preview unavailable: {ex.Message}";
            BeforePreviewUri = null;
            AfterPreviewUri = null;
        }
    }

    private static string? BuildPreviewFilters(MediaInfo mediaInfo, IReadOnlyList<IVideoOperation> operations)
    {
        var context = new VideoOperationContext();
        foreach (var operation in operations)
        {
            operation.Apply(mediaInfo, context);
        }

        if (context.VideoFilters.Count == 0)
        {
            return null;
        }

        return string.Join(',', context.VideoFilters);
    }

    private async Task<Uri?> CreatePreviewFrameAsync(string inputPath, TimeSpan time, string? videoFilters)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "reelpress-preview", $"{Guid.NewGuid():N}.jpg");
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var args = new List<string>
        {
            "-y",
            "-ss", FormatFfmpegTime(time),
            "-i", inputPath
        };

        if (!string.IsNullOrWhiteSpace(videoFilters))
        {
            args.Add("-vf");
            args.Add(videoFilters);
        }

        args.Add("-frames:v");
        args.Add("1");
        args.Add("-q:v");
        args.Add("2");
        args.Add(outputPath);

        var result = await _ffmpegEngine.RunFfmpegAsync(args).ConfigureAwait(true);
        if (!result.Success || !File.Exists(outputPath))
        {
            return null;
        }

        return new Uri(outputPath);
    }

    private static string FormatFfmpegTime(TimeSpan value)
    {
        var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return clamped.ToString(@"hh\\:mm\\:ss\\.fff", System.Globalization.CultureInfo.InvariantCulture);
    }
}
