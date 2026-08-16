namespace ReelPress.Core;

public enum BatchItemStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Skipped = 3,
    Failed = 4,
    Canceled = 5
}
