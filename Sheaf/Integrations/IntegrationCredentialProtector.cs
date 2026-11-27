using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;

using Sheaf.Data;

namespace Sheaf.Integrations;

/// <summary>
/// Encrypts integration credentials at rest with the application's Data Protection keys. Each provider gets its own
/// purpose, so one provider's ciphertext cannot be read back as another's.
/// </summary>
public sealed class IntegrationCredentialProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _root = provider.CreateProtector("Sheaf.Integrations.Credentials.v1");
    private readonly ConcurrentDictionary<IntegrationProvider, IDataProtector> _protectors = new();

    public string Protect<T>(IntegrationProvider integration, T credentials) =>
        For(integration).Protect(JsonSerializer.Serialize(credentials));

    /// <summary>Returns null for a missing value or one the current keys cannot decrypt, such as after a key loss.</summary>
    public T? TryUnprotect<T>(IntegrationProvider integration, string? protectedCredentials) where T : class
    {
        if (protectedCredentials is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(For(integration).Unprotect(protectedCredentials));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private IDataProtector For(IntegrationProvider integration) =>
        _protectors.GetOrAdd(integration, key => _root.CreateProtector(key.ToString()));
}
