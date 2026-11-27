using Microsoft.Extensions.Hosting.Internal;

using Sheaf.Integrations.PlanningCenter;

namespace Sheaf.Api.Tests.Integrations;

public class PlanningCenterOptionsValidatorTests
{
    private static readonly Uri Callback = new("https://sheaf.example.org/api/integrations/planning-center/callback");

    [Test]
    public async Task Validate_NothingConfigured_Succeeds()
    {
        var result = Validator("Production").Validate(null, new PlanningCenterOptions());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments("client", null, true)]
    [Arguments(null, "secret", true)]
    [Arguments("client", "secret", false)]
    public async Task Validate_PartlyConfigured_Fails(string? clientId, string? clientSecret, bool withRedirect)
    {
        PlanningCenterOptions options = new()
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = withRedirect ? Callback : null
        };

        var result = Validator("Development").Validate(null, options);

        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_ProductionWithoutUserAgentContact_Fails()
    {
        var result = Validator("Production").Validate(null, Configured("Sheaf/1.0"));

        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    [Arguments("Sheaf (ops@example.org)")]
    [Arguments("Sheaf (https://sheaf.example.org/contact)")]
    public async Task Validate_ProductionWithUserAgentContact_Succeeds(string userAgent)
    {
        var result = Validator("Production").Validate(null, Configured(userAgent));

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_DevelopmentWithDefaultUserAgent_Succeeds()
    {
        var result = Validator("Development").Validate(null, Configured("Sheaf/1.0"));

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments("file:///api/integrations/planning-center/callback")]
    [Arguments("http://sheaf.example.org/api/integrations/planning-center/callback")]
    public async Task Validate_RedirectUriNotHttpsOrLocalhost_Fails(string redirectUri)
    {
        PlanningCenterOptions options = Configured("Sheaf (ops@example.org)");
        options.RedirectUri = new Uri(redirectUri);

        var result = Validator("Production").Validate(null, options);

        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_LocalhostHttpRedirectUri_Succeeds()
    {
        PlanningCenterOptions options = Configured("Sheaf (ops@example.org)");
        options.RedirectUri = new Uri("http://localhost:5285/api/integrations/planning-center/callback");

        var result = Validator("Development").Validate(null, options);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_UnconfiguredWithMalformedUserAgent_Fails()
    {
        // The HTTP client sets this header even without an OAuth application, so a bad value must fail at start.
        var result = Validator("Development").Validate(null, new PlanningCenterOptions { UserAgent = "Sheaf (unclosed" });

        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_ConfiguredWithoutReturnUrl_Fails()
    {
        PlanningCenterOptions options = Configured("Sheaf (ops@example.org)");
        options.ReturnUrl = null;

        var result = Validator("Development").Validate(null, options);

        await Assert.That(result.Failed).IsTrue();
    }

    private static PlanningCenterOptions Configured(string userAgent) => new()
    {
        ClientId = "client",
        ClientSecret = "secret",
        RedirectUri = Callback,
        ReturnUrl = "https://sheaf.example.org/settings/integrations",
        UserAgent = userAgent
    };

    private static PlanningCenterOptionsValidator Validator(string environment) =>
        new(new HostingEnvironment { EnvironmentName = environment });
}
