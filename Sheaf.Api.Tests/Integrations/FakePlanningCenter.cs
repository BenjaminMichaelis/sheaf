using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Sheaf.Api.Tests.Integrations;

/// <summary>
/// Stands in for api.planningcenteronline.com behind the OAuth HTTP client. It serves an OpenID Connect discovery
/// document and signing key shaped like Planning Center's, and by default issues numbered tokens (access-1/refresh-1,
/// access-2/refresh-2, ...); a code exchange also returns an RS256 id token for <see cref="OrganizationId"/> carrying
/// <see cref="Nonce"/>. Tests queue one-off responses per path to simulate failures.
/// </summary>
public sealed class FakePlanningCenter : HttpMessageHandler
{
    public const string Issuer = "https://api.planningcenteronline.com";

    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<Task<HttpResponseMessage>>>> _overrides = new();
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _signingKey;
    private int _tokenNumber;

    public FakePlanningCenter() => _signingKey = new RsaSecurityKey(_rsa) { KeyId = "test-key" };

    public sealed record Request(HttpMethod Method, string Path, IReadOnlyDictionary<string, string> Form, string? Authorization);

    public ConcurrentQueue<Request> Requests { get; } = new();

    public string OrganizationId { get; set; } = "4242";

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
            "/.well-known/openid-configuration" => Json(HttpStatusCode.OK, $$"""
                {"issuer":"{{Issuer}}","authorization_endpoint":"{{Issuer}}/oauth/authorize","token_endpoint":"{{Issuer}}/oauth/token",
                 "userinfo_endpoint":"{{Issuer}}/oauth/userinfo","jwks_uri":"{{Issuer}}/oauth/discovery/keys",
                 "response_types_supported":["code"],"response_modes_supported":["query","fragment","form_post"],
                 "subject_types_supported":["public"],"id_token_signing_alg_values_supported":["RS256"],
                 "code_challenge_methods_supported":["S256"],"token_endpoint_auth_methods_supported":["client_secret_basic","client_secret_post"]}
                """),
            "/oauth/discovery/keys" => Json(HttpStatusCode.OK, Jwks()),
            "/oauth/token" => IssueTokens(form.GetValueOrDefault("grant_type") == "authorization_code"),
            "/oauth/revoke" => Json(HttpStatusCode.OK, "{}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    private HttpResponseMessage IssueTokens(bool withIdToken)
    {
        int number = Interlocked.Increment(ref _tokenNumber);
        string idToken = withIdToken ? $$""","id_token":"{{IdToken()}}" """ : "";
        HttpResponseMessage response = Json(HttpStatusCode.OK,
            $$"""{"access_token":"access-{{number}}","token_type":"bearer","expires_in":7200,"refresh_token":"refresh-{{number}}","scope":"openid people","created_at":1700000000{{idToken}}}""");
        response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        return response;
    }

    /// <summary>An id token shaped like Planning Center's documented one, with its organization id as a number.</summary>
    private string IdToken()
    {
        DateTime now = DateTime.UtcNow;
        Dictionary<string, object> claims = new()
        {
            ["sub"] = "77",
            ["name"] = "Pat Admin",
            ["email"] = "pat@example.com",
            ["organization_id"] = long.Parse(OrganizationId, System.Globalization.CultureInfo.InvariantCulture),
            ["organization_name"] = "Grace Church"
        };
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

    private string Jwks()
    {
        RSAParameters key = _rsa.ExportParameters(includePrivateParameters: false);
        return $$"""{"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"{{_signingKey.KeyId}}","n":"{{Base64Url.EncodeToString(key.Modulus)}}","e":"{{Base64Url.EncodeToString(key.Exponent)}}"}]}""";
    }

}
