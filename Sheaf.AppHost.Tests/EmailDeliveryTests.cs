using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using TUnit.Aspire;

namespace Sheaf.AppHost.Tests;

/// <summary>Account email reaches the local Mailpit server through the backend's SMTP transport.</summary>
[ClassDataSource<AppHostFixture>(Shared = SharedType.PerTestSession)]
public partial class EmailDeliveryTests(AppHostFixture fixture)
{
    [Test]
    public async Task Register_NewAddress_DeliversVerificationCodeToMailpit()
    {
        string email = $"{Guid.NewGuid():N}@example.com";
        using HttpClient backend = fixture.CreateHttpClient("Sheaf-backend", "http");
        using HttpClient mailpit = fixture.CreateHttpClient("mailpit", "http");
        // The API accepts state-changing requests only from known origins, its own included.
        backend.DefaultRequestHeaders.Add("Origin", backend.BaseAddress?.GetLeftPart(UriPartial.Authority));

        using HttpResponseMessage registered = await backend.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Mailpit-test-Password1!",
            confirmPassword = "Mailpit-test-Password1!",
            firstName = "Mia",
            lastName = "Mail"
        });

        await Assert.That(registered.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        string? snippet = await WaitForMessageAsync(mailpit, email);
        await Assert.That(snippet).IsNotNull();
        await Assert.That(CodePattern().IsMatch(snippet ?? "")).IsTrue();
    }

    /// <summary>Polls Mailpit's search API until a message to <paramref name="email"/> arrives; returns its text snippet.</summary>
    private static async Task<string?> WaitForMessageAsync(HttpClient mailpit, string email)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using JsonDocument found = JsonDocument.Parse(
                await mailpit.GetStringAsync($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{email}\"")}"));
            if (found.RootElement.GetProperty("messages").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } message)
            {
                return message.GetProperty("Snippet").GetString();
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        return null;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();
}
