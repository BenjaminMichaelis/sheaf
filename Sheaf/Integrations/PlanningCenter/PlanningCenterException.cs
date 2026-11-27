namespace Sheaf.Integrations.PlanningCenter;

public enum PlanningCenterFailure
{
    /// <summary>Sheaf has no Planning Center OAuth application configured.</summary>
    NotConfigured,
    /// <summary>The church has no active Planning Center connection.</summary>
    NotConnected,
    /// <summary>The request named a different church than the one the signed-in session has selected.</summary>
    ChurchMismatch
}

public sealed class PlanningCenterException(PlanningCenterFailure failure) : Exception(failure.ToString())
{
    public PlanningCenterFailure Failure { get; } = failure;
}
