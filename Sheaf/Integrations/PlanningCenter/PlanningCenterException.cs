namespace Sheaf.Integrations.PlanningCenter;

public enum PlanningCenterFailure
{
    /// <summary>Sheaf has no Planning Center OAuth application configured.</summary>
    NotConfigured,
    /// <summary>The church has no active Planning Center connection.</summary>
    NotConnected
}

public sealed class PlanningCenterException(PlanningCenterFailure failure) : Exception(failure.ToString())
{
    public PlanningCenterFailure Failure { get; } = failure;
}
