namespace Sheaf.Middleware;

internal sealed class AllowedOriginPolicy
{
    private readonly HashSet<string> _origins;
    private readonly bool _development;

    public AllowedOriginPolicy(IConfiguration configuration, IHostEnvironment environment, bool documentGeneration)
    {
        _development = environment.IsDevelopment();
        string[] origins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        if (!_development && !documentGeneration && origins.Length == 0)
        {
            throw new InvalidOperationException("AllowedOrigins must contain at least one explicit HTTP(S) origin outside Development.");
        }

        _origins = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (string origin in origins)
        {
            if (!TryGetOrigin(origin, out string normalized))
            {
                throw new InvalidOperationException("AllowedOrigins must contain only HTTP(S) origins without paths, credentials, queries or fragments.");
            }
            _origins.Add(normalized);
        }
    }

    public bool IsAllowed(string origin) => TryGetOrigin(origin, out string normalized)
        && (_origins.Contains(normalized)
            || (_development && new Uri(normalized).Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)));

    public bool IsAllowedForRequest(string origin, HttpRequest request) => TryGetOrigin(origin, out string normalized)
        && (IsAllowed(normalized)
            || (TryGetOrigin($"{request.Scheme}://{request.Host}", out string apiOrigin)
                && normalized.Equals(apiOrigin, StringComparison.OrdinalIgnoreCase)));

    internal static bool TryGetOrigin(string value, out string origin, bool allowPath = false)
    {
        origin = "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)
            || (!allowPath && (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))))
        {
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
