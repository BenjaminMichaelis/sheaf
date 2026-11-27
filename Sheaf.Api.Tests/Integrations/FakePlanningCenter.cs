using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Sheaf.Api.Tests.Integrations;

/// <summary>
/// Stands in for api.planningcenteronline.com behind the OAuth HTTP client. It serves an OpenID Connect discovery
/// document and signing key shaped like Planning Center's, and accepts revokes. Tests queue each code exchange's response,
/// usually <see cref="CodeExchange"/>: tokens plus an RS256 id token for <see cref="OrganizationId"/> carrying
/// <see cref="Nonce"/>, and queue other one-off responses to simulate failures.
/// </summary>
public sealed class FakePlanningCenter : HttpMessageHandler
{
    public const string Issuer = "https://api.planningcenteronline.com";

    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<Task<HttpResponseMessage>>>> _overrides = new();
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _signingKey;

    public FakePlanningCenter() => _signingKey = new RsaSecurityKey(_rsa) { KeyId = "test-key" };

    public sealed record Request(HttpMethod Method, string Path, IReadOnlyDictionary<string, string> Form, string? Authorization);

    public ConcurrentQueue<Request> Requests { get; } = new();

    public string OrganizationId { get; set; } = "4242";

    /// <summary>The <c>sub</c> of the person who authorizes.</summary>
    public string PersonId { get; set; } = "77";

    /// <summary>Whether id tokens carry the organization claims; false simulates a token Sheaf cannot use.</summary>
    public bool IncludeOrganization { get; set; } = true;

    /// <summary>The nonce from the latest authorization request, which the next id token must carry.</summary>
    public string? Nonce { get; set; }

    public string ClientId { get; set; } = "test-client";

    public IReadOnlyList<Request> To(string path) => [.. Requests.Where(r => r.Path == path)];

    public void Enqueue(string path, Func<HttpResponseMessage> response) =>
        Enqueue(path, () => Task.FromResult(response()));

    /// <summary>Queues a response that can do work first, such as changing the database while a request is in flight.</summary>
    public void Enqueue(string path, Func<Task<HttpResponseMessage>> response) =>
        _overrides.GetOrAdd(path, _ => new()).Enqueue(response);

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    /// <summary>A code-exchange response with the given tokens and a valid id token.</summary>
    public HttpResponseMessage CodeExchange(string accessToken, string refreshToken) => Json(HttpStatusCode.OK,
        $$"""{"access_token":"{{accessToken}}","token_type":"bearer","expires_in":7200,"refresh_token":"{{refreshToken}}","scope":"openid people","id_token":"{{IdToken()}}"}""");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri?.AbsolutePath ?? "";
        Dictionary<string, string> form = request.Content is null
            ? []
            : QueryHelpers.ParseQuery(await request.Content.ReadAsStringAsync(cancellationToken))
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        Requests.Enqueue(new(request.Method, path, form, request.Headers.Authorization?.ToString()));

        if (_overrides.TryGetValue(path, out var queue) && queue.TryDequeue(out var response))
            return await response();

        return path switch
        {
            "/.well-known/openid-configuration" => Json(HttpStatusCode.OK, Discovery),
            "/oauth/discovery/keys" => Json(HttpStatusCode.OK, Jwks()),
            "/oauth/revoke" => Json(HttpStatusCode.OK, "{}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    /// <summary>An id token shaped like Planning Center's documented one, with its organization id as a number.</summary>
    private string IdToken()
    {
        DateTime now = DateTime.UtcNow;
        Dictionary<string, object> claims = new()
        {
            ["sub"] = PersonId,
            ["name"] = "Pat Admin",
            ["email"] = "pat@example.com"
        };
        if (IncludeOrganization)
        {
            claims["organization_id"] = long.Parse(OrganizationId, System.Globalization.CultureInfo.InvariantCulture);
            claims["organization_name"] = "Grace Church";
        }
        if (Nonce is not null)
            claims["nonce"] = Nonce;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddHours(1),
            Claims = claims,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256)
        });
    }

    /// <summary>The parts of Planning Center's published discovery document that Sheaf relies on.</summary>
    private static readonly string Discovery = OpenIdConnectConfiguration.Write(new OpenIdConnectConfiguration
    {
        Issuer = Issuer,
        AuthorizationEndpoint = $"{Issuer}/oauth/authorize",
        TokenEndpoint = $"{Issuer}/oauth/token",
        RevocationEndpoint = $"{Issuer}/oauth/revoke",
        JwksUri = $"{Issuer}/oauth/discovery/keys",
        ResponseTypesSupported = { "code" },
        ResponseModesSupported = { "query", "fragment", "form_post" },
        SubjectTypesSupported = { "public" },
        IdTokenSigningAlgValuesSupported = { SecurityAlgorithms.RsaSha256 },
        CodeChallengeMethodsSupported = { "S256" },
        TokenEndpointAuthMethodsSupported = { "client_secret_basic", "client_secret_post" }
    });

    /// <summary>The signing key set, built from the public half of the key only.</summary>
    private string Jwks()
    {
        RsaSecurityKey publicKey = new(_rsa.ExportParameters(includePrivateParameters: false)) { KeyId = _signingKey.KeyId };
        JsonWebKey key = JsonWebKeyConverter.ConvertFromRSASecurityKey(publicKey);
        key.Use = JsonWebKeyUseNames.Sig;
        key.Alg = SecurityAlgorithms.RsaSha256;
        return JsonSerializer.Serialize(new { keys = new[] { key } }, JwksJson);
    }

    private static readonly JsonSerializerOptions JwksJson = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
