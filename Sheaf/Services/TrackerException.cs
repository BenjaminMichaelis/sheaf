namespace Sheaf.Services;

public enum TrackerFailure
{
    NotFound,
    Forbidden,
    InvalidInput,
    Conflict,
    /// <summary>The If-Match version is not the resource's current version.</summary>
    PreconditionFailed,
    /// <summary>A write that requires If-Match was sent without it.</summary>
    PreconditionRequired
}

public sealed class TrackerException(TrackerFailure failure) : Exception(failure.ToString())
{
    public TrackerFailure Failure { get; } = failure;
}
