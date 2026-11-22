namespace Sheaf.Services;

public enum TrackerFailure
{
    NotFound,
    Forbidden,
    InvalidInput,
    Conflict
}

public sealed class TrackerException(TrackerFailure failure) : Exception(failure.ToString())
{
    public TrackerFailure Failure { get; } = failure;
}
