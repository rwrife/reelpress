namespace ReelPress.Core;

public sealed class PipelineRunner
{
    private readonly IFfmpegEngine _ffmpegEngine;
    private readonly IMediaProbe _mediaProbe;

    public PipelineRunner(IFfmpegEngine ffmpegEngine, IMediaProbe mediaProbe)
    {
        _ffmpegEngine = ffmpegEngine ?? throw new ArgumentNullException(nameof(ffmpegEngine));
        _mediaProbe = mediaProbe ?? throw new ArgumentNullException(nameof(mediaProbe));
    }

    public async Task<IReadOnlyList<BatchItemResult>> RunAsync(
        IEnumerable<BatchJob> jobs,
        int maxConcurrency = 2,
        IProgress<BatchJobProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be greater than zero.");
        }

        var jobList = jobs.ToList();
        if (jobList.Count == 0)
        {
            return Array.Empty<BatchItemResult>();
        }

        for (var i = 0; i < jobList.Count; i++)
        {
            var queued = jobList[i];
            progress?.Report(new BatchJobProgress(
                JobIndex: i,
                TotalJobs: jobList.Count,
                InputPath: queued.InputPath,
                Status: BatchItemStatus.Queued,
                Percent: null,
                Message: queued.DisplayName ?? Path.GetFileName(queued.InputPath)));
        }

        var results = new BatchItemResult[jobList.Count];
        using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = jobList.Select((job, index) => RunOneWithGateAsync(job, index, jobList.Count, gate, results, progress, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);

        return results;
    }

    private async Task RunOneWithGateAsync(
        BatchJob job,
        int index,
        int totalJobs,
        SemaphoreSlim gate,
        BatchItemResult[] results,
        IProgress<BatchJobProgress>? progress,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            results[index] = await RunOneAsync(job, index, totalJobs, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BatchItemResult> RunOneAsync(
        BatchJob job,
        int index,
        int totalJobs,
        IProgress<BatchJobProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateJob(job);

        var startedAt = DateTimeOffset.UtcNow;

        progress?.Report(new BatchJobProgress(
            JobIndex: index,
            TotalJobs: totalJobs,
            InputPath: job.InputPath,
            Status: BatchItemStatus.Running,
            Percent: 0,
            Message: "Probing media"));

        try
        {
            var mediaInfo = await _mediaProbe.ProbeAsync(job.InputPath, cancellationToken).ConfigureAwait(false);

            var validationErrors = VideoOperationPlanner.Validate(mediaInfo, job.Operations);
            if (validationErrors.Count > 0)
            {
                var endedAt = DateTimeOffset.UtcNow;
                var validationMessage = string.Join("; ", validationErrors);

                progress?.Report(new BatchJobProgress(
                    JobIndex: index,
                    TotalJobs: totalJobs,
                    InputPath: job.InputPath,
                    Status: BatchItemStatus.Skipped,
                    Percent: null,
                    Message: validationMessage));

                return new BatchItemResult(
                    InputPath: job.InputPath,
                    OutputPath: job.OutputPath,
                    Status: BatchItemStatus.Skipped,
                    Message: validationMessage,
                    StartedAtUtc: startedAt,
                    EndedAtUtc: endedAt);
            }

            var arguments = VideoOperationPlanner.BuildArguments(
                mediaInfo,
                job.InputPath,
                job.OutputPath,
                job.Operations.ToArray());

            var ffmpegProgress = new Progress<FfmpegProgress>(eventArgs =>
            {
                progress?.Report(new BatchJobProgress(
                    JobIndex: index,
                    TotalJobs: totalJobs,
                    InputPath: job.InputPath,
                    Status: BatchItemStatus.Running,
                    Percent: eventArgs.Percentage,
                    Message: eventArgs.RawLine,
                    ProcessedTime: eventArgs.ProcessedTime));
            });

            TimeSpan? expectedDuration = mediaInfo.Duration > TimeSpan.Zero
                ? mediaInfo.Duration
                : null;

            var runResult = await _ffmpegEngine
                .RunFfmpegAsync(arguments, expectedDuration, ffmpegProgress, cancellationToken)
                .ConfigureAwait(false);

            var status = runResult switch
            {
                { WasCanceled: true } => BatchItemStatus.Canceled,
                { Success: true } => BatchItemStatus.Succeeded,
                _ => BatchItemStatus.Failed
            };

            var message = status == BatchItemStatus.Succeeded
                ? "Completed"
                : Tail(runResult.StdErr, 12);

            progress?.Report(new BatchJobProgress(
                JobIndex: index,
                TotalJobs: totalJobs,
                InputPath: job.InputPath,
                Status: status,
                Percent: status == BatchItemStatus.Succeeded ? 100 : null,
                Message: message));

            return new BatchItemResult(
                InputPath: job.InputPath,
                OutputPath: job.OutputPath,
                Status: status,
                Message: message,
                StartedAtUtc: startedAt,
                EndedAtUtc: DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new BatchJobProgress(
                JobIndex: index,
                TotalJobs: totalJobs,
                InputPath: job.InputPath,
                Status: BatchItemStatus.Canceled,
                Percent: null,
                Message: "Canceled"));

            return new BatchItemResult(
                InputPath: job.InputPath,
                OutputPath: job.OutputPath,
                Status: BatchItemStatus.Canceled,
                Message: "Canceled",
                StartedAtUtc: startedAt,
                EndedAtUtc: DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            progress?.Report(new BatchJobProgress(
                JobIndex: index,
                TotalJobs: totalJobs,
                InputPath: job.InputPath,
                Status: BatchItemStatus.Failed,
                Percent: null,
                Message: ex.Message));

            return new BatchItemResult(
                InputPath: job.InputPath,
                OutputPath: job.OutputPath,
                Status: BatchItemStatus.Failed,
                Message: ex.Message,
                StartedAtUtc: startedAt,
                EndedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    private static void ValidateJob(BatchJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (string.IsNullOrWhiteSpace(job.InputPath))
        {
            throw new ArgumentException("Batch job input path is required.", nameof(job));
        }

        if (string.IsNullOrWhiteSpace(job.OutputPath))
        {
            throw new ArgumentException("Batch job output path is required.", nameof(job));
        }

        if (job.Operations is null || job.Operations.Count == 0)
        {
            throw new ArgumentException("Batch job must contain at least one operation.", nameof(job));
        }
    }

    private static string Tail(string? text, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "No ffmpeg stderr output.";
        }

        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(Math.Max(1, maxLines));

        return string.Join(Environment.NewLine, lines);
    }
}
