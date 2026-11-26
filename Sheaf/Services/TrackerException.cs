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
    PreconditionRequired,
    /// <summary>
    /// The write looks like a mistake (a likely duplicate). Nothing was saved; the problem's extensions say why, and
    /// the client resends with <c>confirm: true</c> to save anyway.
    /// </summary>
    ConfirmationRequired
}

public sealed class TrackerException(TrackerFailure failure, IDictionary<string, object?>? extensions = null) : Exception(failure.ToString())
{
    /// <summary>The ProblemDetails type of <see cref="TrackerFailure.ConfirmationRequired"/>, so clients can recognize it.</summary>
    public const string ConfirmationRequiredType = "urn:sheaf:problem:confirmation-required";

    public TrackerFailure Failure { get; } = failure;

    /// <summary>Extra ProblemDetails members, such as the possible duplicates behind <see cref="TrackerFailure.ConfirmationRequired"/>.</summary>
    public IDictionary<string, object?>? Extensions { get; } = extensions;
}
