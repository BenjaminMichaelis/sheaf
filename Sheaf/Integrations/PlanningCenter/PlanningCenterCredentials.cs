namespace Sheaf.Integrations.PlanningCenter;

/// <summary>What Sheaf stores, encrypted, in <see cref="Data.ChurchIntegration.ProtectedCredentials"/> for Planning Center.</summary>
/// <param name="RefreshTokenIssuedAt">Planning Center honors a refresh token for 90 days and issues a new one on every refresh.</param>
public sealed record PlanningCenterCredentials(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenIssuedAt)
{
    public static PlanningCenterCredentials From(PlanningCenterTokenSet tokenSet, DateTimeOffset now) =>
        new(tokenSet.AccessToken, now + tokenSet.ExpiresIn, tokenSet.RefreshToken, now);
}
