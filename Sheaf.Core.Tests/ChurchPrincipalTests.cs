using System.Security.Claims;

using Sheaf.Core.Authentication;
using Sheaf.Data;

namespace Sheaf.Core.Tests;

public class ChurchPrincipalTests
{
    [Test]
    public async Task ReadChurchClaims_CompleteMembershipTuple_ReturnsSelection()
    {
        Guid churchId = Guid.NewGuid();
        Guid membershipId = Guid.NewGuid();
        ClaimsPrincipal principal = CreatePrincipal(churchId.ToString("D"), membershipId.ToString("D"), "Coordinator");

        ChurchClaimState state = principal.ReadChurchClaims(out ChurchSelection? selection);

        await Assert.That(state).IsEqualTo(ChurchClaimState.Selected);
        await Assert.That(selection).IsEqualTo(new ChurchSelection(churchId, membershipId, ChurchMembershipRole.Coordinator));
    }

    [Test]
    public async Task ReadChurchClaims_NoChurchAuthority_ReturnsUnselected()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "Identity.Application"));

        await Assert.That(principal.ReadChurchClaims(out ChurchSelection? selection)).IsEqualTo(ChurchClaimState.Unselected);
        await Assert.That(selection).IsNull();
    }

    [Test]
    [Arguments("missing-church")]
    [Arguments("missing-membership")]
    [Arguments("missing-role")]
    [Arguments("duplicate-church")]
    [Arguments("duplicate-membership")]
    [Arguments("duplicate-role")]
    [Arguments("empty-church")]
    [Arguments("empty-membership")]
    [Arguments("malformed-id")]
    [Arguments("empty-role")]
    [Arguments("numeric-role")]
    [Arguments("unknown-role")]
    [Arguments("wrong-case-role")]
    [Arguments("legacy-role")]
    [Arguments("split-identities")]
    [Arguments("secondary-identity")]
    [Arguments("anonymous")]
    public async Task ReadChurchClaims_InvalidTuple_ReturnsInvalidWithoutSelection(string scenario)
    {
        ClaimsPrincipal principal = CreatePrincipal(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), "Leader");
        ClaimsIdentity identity = principal.Identities.Single();

        string? claimType = scenario switch
        {
            "missing-church" or "duplicate-church" or "empty-church" or "malformed-id" => SheafClaimTypes.ChurchId,
            "missing-membership" or "duplicate-membership" or "empty-membership" => SheafClaimTypes.ChurchMembershipId,
            "missing-role" or "duplicate-role" or "empty-role" or "numeric-role" or "unknown-role"
                or "wrong-case-role" or "split-identities" => SheafClaimTypes.Role,
            _ => null
        };
        if (claimType is not null)
        {
            Claim claim = identity.FindFirst(claimType)
                ?? throw new InvalidOperationException("The test tuple is incomplete.");
            if (scenario.StartsWith("duplicate-", StringComparison.Ordinal))
            {
                identity.AddClaim(new Claim(claim.Type, claim.Value));
            }
            else
            {
                identity.RemoveClaim(claim);
                string? replacement = scenario switch
                {
                    "empty-church" or "empty-membership" => Guid.Empty.ToString("D"),
                    "malformed-id" => "not-a-guid",
                    "empty-role" => "",
                    "numeric-role" => "0",
                    "unknown-role" => "Owner",
                    "wrong-case-role" => "leader",
                    _ => null
                };
                if (replacement is not null)
                {
                    identity.AddClaim(new Claim(claim.Type, replacement));
                }
                else if (scenario == "split-identities")
                {
                    principal.AddIdentity(new ClaimsIdentity([new Claim(claim.Type, claim.Value)], "Other"));
                }
            }
        }
        else if (scenario == "legacy-role")
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        }
        else if (scenario == "anonymous")
        {
            principal = new ClaimsPrincipal(new ClaimsIdentity(identity.Claims));
        }
        else if (scenario == "secondary-identity")
        {
            principal = new ClaimsPrincipal(
            [
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "Identity.Application"),
                identity
            ]);
        }

        await Assert.That(principal.ReadChurchClaims(out ChurchSelection? selection)).IsEqualTo(ChurchClaimState.Invalid);
        await Assert.That(selection).IsNull();
    }

    private static ClaimsPrincipal CreatePrincipal(string churchId, string membershipId, string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(SheafClaimTypes.ChurchId, churchId),
            new Claim(SheafClaimTypes.ChurchMembershipId, membershipId),
            new Claim(SheafClaimTypes.Role, role)
        ], "Identity.Application", ClaimTypes.Name, SheafClaimTypes.Role));
}
